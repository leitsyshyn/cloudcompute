# syntax=docker/dockerfile:1
# Sequential word-count baseline (tools/WordCount.Seq). Build context: the repository root.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /work
COPY src/WordCount.Core/ src/WordCount.Core/
COPY tools/WordCount.Seq/ tools/WordCount.Seq/
RUN dotnet publish tools/WordCount.Seq/WordCount.Seq.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION}
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "WordCount.Seq.dll"]
