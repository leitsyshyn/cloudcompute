# syntax=docker/dockerfile:1
#
# Builds the parcs8 images (daemon, host, portal) from source.
# Build context: third_party/parcs8 (see scripts/build-images.sh).
#
# Why not the upstream Dockerfiles:
#   * every project references the NuGet package Parcs.Net 10.1.0, which is not published
#     on nuget.org (latest there is 10.0.0), so `dotnet restore` fails. We pack it from
#     src/Parcs.Net into a local feed first.
#   * the published images on Docker Hub are linux/amd64 only; building from source gives
#     native arm64 images for local runs and amd64 images for the cloud.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS sdk
WORKDIR /src
COPY src/Parcs.Net/ src/Parcs.Net/
# GeneratePackageOnBuild=True in the csproj makes `dotnet pack` look for the dll before it is
# built (NU5026), so it is switched off for the explicit pack.
RUN dotnet pack src/Parcs.Net/Parcs.Net.csproj -c Release -o /feed -p:GeneratePackageOnBuild=false \
 && dotnet nuget add source /feed --name parcs-local
COPY . .

FROM sdk AS build-daemon
RUN dotnet publish src/Parcs.Daemon/Parcs.Daemon.csproj -c Release -o /out /p:UseAppHost=false

FROM sdk AS build-host
RUN dotnet publish src/Parcs.Host/Parcs.Host.csproj -c Release -o /out /p:UseAppHost=false

FROM sdk AS build-portal
RUN dotnet publish src/Parcs.Portal/Parcs.Portal.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS daemon
WORKDIR /app
COPY --from=build-daemon /out .
ENTRYPOINT ["dotnet", "Parcs.Daemon.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS host
WORKDIR /app
COPY --from=build-host /out .
ENTRYPOINT ["dotnet", "Parcs.Host.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS portal
WORKDIR /app
COPY --from=build-portal /out .
ENTRYPOINT ["dotnet", "Parcs.Portal.dll"]
