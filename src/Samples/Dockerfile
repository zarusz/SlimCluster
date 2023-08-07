FROM mcr.microsoft.com/dotnet/sdk:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /source
COPY src ./src
COPY docs ./docs
WORKDIR /source/src
ARG SERVICE
RUN dotnet publish "Samples/${SERVICE}" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ARG SERVICE
ENV SERVICE_FILE "${SERVICE}.dll"
ENTRYPOINT [ "/usr/bin/dotnet", "SlimCluster.Samples.Service.dll" ]
