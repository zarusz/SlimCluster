Invoke-Expression "../Docker-Build.ps1 -ProjectName SlimCluster.Samples.ConsoleApp"

#docker build --build-arg SERVICE=SlimCluster.Samples.ConsoleApp -t zarusz/sc-consoleapp:latest .

# In case you need to debug why it's not starting:
# docker run -it zarusz/consoleapp:latest sh
