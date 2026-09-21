FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/AiDetection.Web/AiDetection.Web.csproj \
    && dotnet publish src/AiDetection.Web/AiDetection.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG MODEL_REVISION=16db135220b318d811b207db576d90368980b595
ARG C2PATOOL_VERSION=0.27.15
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl libgomp1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/models \
    && curl --fail --location --retry 3 \
      "https://huggingface.co/Thermostatic/community-forensics-frontier-detector-2026-08/resolve/${MODEL_REVISION}/community_forensics_frontier_fp16.onnx" \
      --output /app/models/community_forensics_frontier_fp16.onnx \
    && echo "d75791ba2fa59146025d342cfaafa9ddeab24af117642a94f752ee4c1619375d  /app/models/community_forensics_frontier_fp16.onnx" | sha256sum --check --status \
    && curl --fail --location --retry 3 \
      "https://github.com/contentauth/c2pa-rs/releases/download/c2patool-v${C2PATOOL_VERSION}/c2patool-v${C2PATOOL_VERSION}-x86_64-unknown-linux-gnu.tar.gz" \
      --output /tmp/c2patool.tar.gz \
    && tar -xzf /tmp/c2patool.tar.gz -C /usr/local/bin --strip-components=1 c2patool/c2patool \
    && rm /tmp/c2patool.tar.gz \
    && chmod +x /usr/local/bin/c2patool

ENV ASPNETCORE_URLS=http://+:8080 \
    DETECTOR_CLASSIFIER=onnx \
    DETECTOR_MODEL_PATH=/app/models/community_forensics_frontier_fp16.onnx \
    C2PATOOL_PATH=/usr/local/bin/c2patool \
    C2PA_TRUST_FILE=/app/c2pa_trust/C2PA-TRUST-LIST.pem
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AiDetection.Web.dll"]
