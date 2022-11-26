param ($ProjectName)

$TagName = $ProjectName.Replace('.','_').ToLower();
docker build --build-arg SERVICE=${ProjectName} -t zarusz/${TagName}:latest -f ../Dockerfile ../../

# In case you need to debug why it's not starting:
# docker run -it zarusz/consoleapp:latest sh
