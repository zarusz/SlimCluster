#docker build --build-arg SERVICE=SlimCluster.Samples.ConsoleApp -t zarusz/consoleapp:1.0.0 .

if ($?) {
	# run with tty console, interactive mode, and remove container after program ends
	docker run -it --rm zarusz/sc-consoleapp:1.0.0
}

# In case you need to debug why it's not starting:
# docker run -it zarusz/consoleapp:latest sh
