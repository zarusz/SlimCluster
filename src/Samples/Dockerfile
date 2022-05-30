FROM mcr.microsoft.com/dotnet/sdk:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR src
COPY . ./
RUN dotnet restore

FROM build AS publish
ARG SERVICE
RUN dotnet publish "Samples/${SERVICE}" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ARG SERVICE
ENV SERVICE_FILE "${SERVICE}.dll"
CMD dotnet $SERVICE_FILE
#ENTRYPOINT ["dotnet", $SERVICE_FILE]
