# Pipes

## Demonstration of piping data

```
# Terminal 1
mkfifo t1.fifo
>> t1.fifo

# Terminal 2
mkfifo t2.fifo
cat t1.fifo | capitalize | tee t2.fifo

# Terminal 3
cat t2.fifo
```

## Run capitalize
```
dotnet run --project ./src/capitalize
```

## Publish and install on macOS
```
dotnet publish -r osx-x64 --self-contained
sudo mkdir /opt/capitalize
sudo cp src/capitalize/bin/Release/net8.0/osx-x64/publish/* /opt/capitalize
sudo ln -s /opt/capitalize /usr/local/bin/capitalize
```

