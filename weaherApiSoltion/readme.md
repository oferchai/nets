# Demo Rest server with Opentelemetry

- Include Tracing with Jaeger
- Include SQL db connection
- using browser internal caching usage with Delta nuget (304)
- Orchestrating with docker compose
- Using Core EF for DB initialization and usage 
- Using scalar , replacing the SwashBuckle
- Option: add Prometheus to the docker compose and initialize in the same manner 

### future improvement , create a Aspire version 
### future improvement: try running in dev containers
To start , run the 'docker-compose up' in the WetherApi folder
Then simply run the client.

- WetherApi runs on http 5179
- Jaeger runs on http 16686