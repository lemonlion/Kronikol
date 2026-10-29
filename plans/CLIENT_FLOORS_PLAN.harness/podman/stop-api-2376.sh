#!/bin/sh
# Stop the API podman-api-2376.sh started (the [6] keeps pkill from matching its own command line).
pkill -f "system service --time=0 tcp://0.0.0.0:237[6]"
