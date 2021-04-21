docker build -t zarusz/consoleapp:1.0.0 --build-arg SERVICE=SlimCluster.Samples.ConsoleApp Samples

if ($?) {
	# run with tty console, interactive mode, and remove container after program ends
	docker run -it --rm zarusz/consoleapp:1.0.0
}

# In case you need to debug why it's not starting:
# docker run -it zarusz/consoleapp:latest sh
