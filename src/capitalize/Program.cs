using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;

namespace capitalize;

class Program
{
    static async Task Main(string[] args)
    {
        var stdout = Console.OpenStandardOutput();
        await ProcessInputAsync(stdout);
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
        
        
        Console.WriteLine("Read from redirected stdin");
        var source = Console.OpenStandardInput();
        writing = FillPipeAsync(source, pipe.Writer);    
        
        Task reading = ReadPipeAsync(pipe.Reader, destination);

        await Task.WhenAll(reading, writing!);
    }
    
    static async Task ProcessWord(ReadOnlySequence<byte> wordSequence, Stream destination)
    {
        var segment = wordSequence.Start;
        while (wordSequence.TryGet(ref segment, out var readOnlyMemorySource, advance: true))
        {
            var word = Encoding.UTF8.GetString(readOnlyMemorySource.Span);
            
            // Transform
            word = word.ToUpper();
            
            var readOnlyMemoryDestination = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(word));
            
            await destination.WriteAsync(readOnlyMemoryDestination);
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

            while (TryReadWord(ref buffer, out ReadOnlySequence<byte> line))
            {
                // Process the line.
                await ProcessWord(line, destination);
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

    static bool TryReadWord(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> word)
    {
        // Look for a space or EOL in the buffer.
        SequencePosition? position = buffer.PositionOf((byte)' ');
        if (position == null)
        {
            position = buffer.PositionOf((byte)'\n');
        }

        if (position == null)
        {
            word = default;
            return false;
        }

        // Include the ' ' or \n
        position = buffer.GetPosition(1, position.Value);
        
        // Capture the word
        word = buffer.Slice(0, position.Value);
        
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