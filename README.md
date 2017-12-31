# Space Engineers Radio Connection Protocol
Space Engineers connection protocol based around reliably sending and receiving data across different grids using radio antennas.

## Current Features
1. High level API - requires minimal time to write code to use the protocol
2. Reliable packet delivery - packets are sent until callback response is received
3. Inbuilt symmetric encryption - connection can secured by providing a key to encrypt transmitted data

## Notes
At the moment the script has not been widely tested, and as such may contain bugs that will cause connections to close at some points or script termination.
Any bug reports are welcome to be submitted (include exception from script and brief summary of actions that lead to the script termination).
