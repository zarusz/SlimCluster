FROM mcr.microsoft.com/dotnet/sdk:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /source
COPY src ./src
COPY docs ./docs
WORKDIR /source/src
ARG SERVICE
RUN dotnet publish "Samples/${SERVICE}" -c Release -o /app/publish
RUN mv /app/publish/${SERVICE}.dll /app/publish/MainService.dll && \
    mv /app/publish/${SERVICE}.runtimeconfig.json /app/publish/MainService.runtimeconfig.json

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT [ "/usr/bin/dotnet", "MainService.dll" ]
