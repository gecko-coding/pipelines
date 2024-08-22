# Pipes

Demonstration of piping data

>> t1.out
tail -f t1.out | dotnet run | tee t2.out
tail -f t2.out

OR 

Terminal 1
mkfifo t1.fifo
>> t1.fifo

Terminal 2
mkfifo t2.fifo
cat t1.fifo | dotnet run | tee t2.fifo

Terminal 3
cat t2.fifo


Checkout

tee
expect
unbuffered

Reference:
https://www.baeldung.com/linux/anonymous-named-pipes#:~:text=A%20FIFO%2C%20also%20known%20as,a%20name%20in%20the%20filesystem.
https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines
https://stackoverflow.com/questions/55897200/how-to-use-standard-input-stream-instead-of-console-readkey

https://www.tutorialspoint.com/bash-terminal-redirect-to-another-terminal#:~:text=Bash%20terminal%20redirection%20allows%20you,and%20%222%3E%22%20symbols.