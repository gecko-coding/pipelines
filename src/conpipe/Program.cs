using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.Design;
using System.IO.Pipelines;

namespace conpipe;

/// <summary>
/// The main function of this program is to take input from std in and pipe out as it is ready.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Stream destination = null;
        if (args.Length > 0)
        {
            Console.WriteLine($"Opening {args[0]}");
            destination = File.Open(args[0], FileMode.Append);
        }
        else
        {
            Console.WriteLine($"Opening stdout");
            destination = Console.OpenStandardError();
        }
        
        await ProcessInputAsync(destination!);
    }
    static async Task ProcessData(ReadOnlySequence<byte> chunk, Stream destination)
    {
        var segment = chunk.Start;
        while (chunk.TryGet(ref segment, out var readOnlyMemory, advance: true))
        {
            await destination.WriteAsync(readOnlyMemory);
            await destination.FlushAsync();
        }
    }

    static async Task ProcessInputAsync(Stream destination)
    {
        // Create schedulers
        var writeScheduler = new SingleThreadPipeScheduler();
        var readScheduler = new SingleThreadPipeScheduler();
        
        // The Pipe will start returning incomplete tasks from FlushAsync until
        // the reader examines at least 5 bytes.
        var options = new PipeOptions(pauseWriterThreshold: 10, resumeWriterThreshold: 5,readerScheduler: readScheduler,
            writerScheduler: writeScheduler,
            useSynchronizationContext: false);
        var pipe = new Pipe(options);

        Task? writing = null;
        
        // Input is from a console
        if (Console.IsInputRedirected == false)
        {
            writing = FillPipeFromConsoleAsync(pipe.Writer);    
        }
        
        // Input is from a file
        if (Console.IsInputRedirected == true)
        {
            var source = Console.OpenStandardInput();
            writing = FillPipeAsync(source, pipe.Writer);    
        }
        
        Task reading = ReadPipeAsync(pipe.Reader, destination);

        await Task.WhenAll(reading, writing!);
    }

    static async Task FillPipeFromConsoleAsync(PipeWriter writer)
    {
        while (true)
        {
            try
            {
                if (Console.KeyAvailable == true)
                {
                    Memory<byte> memory = writer.GetMemory(1);
                    var key = Console.ReadKey(false);
                    
                    var c = key.KeyChar;
                    
                    memory.Span[0] = (byte)c;
                    
                    if (char.IsControl(c))
                    {
                        c = (char)key.Key;
                    
                        // Handle special characters ...
                        if (key.Key == ConsoleKey.Backspace)
                        {
                            // Simulate backspace
                            Console.SetCursorPosition(Console.GetCursorPosition().Left - 1, Console.GetCursorPosition().Top);
                            Console.Write("\b \b");
                            
                            memory.Span[0] = (byte)'\b';
                        }
                        
                        // Handle special characters ...
                        if (key.Key == ConsoleKey.Enter)
                        {
                            // Simulate backspace
                            //Console.SetCursorPosition(Console.GetCursorPosition().Left - 1, Console.GetCursorPosition().Top);
                            Console.Write("\n");
                            
                            memory.Span[0] = (byte)'\n';
                        }
                    }

                    // Tell the PipeWriter how much was read from the Socket.
                    writer.Advance(1);
                }
                else
                {
                    await Task.Delay(10);
                }

            }
            catch (Exception ex)
            {
                Console.Write(ex);
                break;
            }

            // Make the data available to the PipeReader.
            FlushResult result = await writer.FlushAsync();

            if (result.IsCompleted)
            {
                break;
            }
        }
    }

    static async Task FillPipeAsync(Stream source, PipeWriter writer)
    {
        const int minimumBufferSize = 512;
        
        while (true)
        {
            // Allocate at least 512 bytes from the PipeWriter.
            Memory<byte> memory = writer.GetMemory(minimumBufferSize);
            try
            {
                int bytesRead = await source.ReadAsync(memory);
                if (bytesRead == 0)
                {
                    break;
                }

                // Tell the PipeWriter how much was read from the Socket.
                writer.Advance(bytesRead);
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                break;
            }

            // Make the data available to the PipeReader.
            FlushResult result = await writer.FlushAsync();

            if (result.IsCompleted)
            {
                break;
            }
        }

        // By completing PipeWriter, tell the PipeReader that there's no more data coming.
        await writer.CompleteAsync();
    }

    static async Task ReadPipeAsync(PipeReader reader, Stream destination)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadChunk(ref buffer, out ReadOnlySequence<byte> chunk))
            {
                // Process the line.
                await ProcessData(chunk, destination);
            }

            // Tell the PipeReader how much of the buffer has been consumed.
            reader.AdvanceTo(buffer.Start, buffer.End);

            // Stop reading if there's no more data coming.
            if (result.IsCompleted)
            {
                break;
            }
        }

        // Mark the PipeReader as complete.
        await reader.CompleteAsync();
    }

    static bool TryReadChunk(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> chunk)
    {
        // Check if no data
        if (buffer.Length == 0)
        {
            chunk = default;
            return false;
        }
        
        // Look for a space or EOL in the buffer.
        SequencePosition? position = buffer.GetPosition(buffer.Length);
        
        // Capture the word
        chunk = buffer.Slice(0, position.Value);
        
        // Move the buffer along
        buffer = buffer.Slice(position.Value);
        
        return true;
    }
}

// Scheduler that async callbacks on a single dedicated thread.
public class SingleThreadPipeScheduler : PipeScheduler
{
    private readonly BlockingCollection<(Action<object> Action, object State)> _queue =
        new BlockingCollection<(Action<object> Action, object State)>();
    private readonly Thread _thread;

    public SingleThreadPipeScheduler()
    {
        _thread = new Thread(DoWork);
        _thread.Start();
    }

    private void DoWork()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            item.Action(item.State);
        }
    }

    public override void Schedule(Action<object?> action, object? state)
    {
        if (state is not null)
        {
            _queue.Add((action, state));
        }
        // else log the fact that _queue.Add was not called.
    }
}
