FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Download Tailwind CLI
RUN curl -sLO https://github.com/tailwindlabs/tailwindcss/releases/download/v3.4.17/tailwindcss-linux-x64 \
    && chmod +x tailwindcss-linux-x64 \
    && mv tailwindcss-linux-x64 /usr/local/bin/tailwindcss

COPY ["AspendoraFileShare.csproj", "./"]
RUN dotnet restore "AspendoraFileShare.csproj"
COPY . .

# Build Tailwind CSS
RUN tailwindcss -i ./Styles/tailwind.css -o ./wwwroot/css/tailwind.min.css --minify

RUN dotnet build "AspendoraFileShare.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AspendoraFileShare.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Generate migration bundle
RUN dotnet tool install --global dotnet-ef --version 9.0.0
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet ef migrations bundle --project AspendoraFileShare.csproj -o /app/publish/efbundle --self-contained

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create startup script that runs migrations then starts app
RUN echo '#!/bin/sh\n\
if [ -f ./efbundle ]; then\n\
    echo "Running database migrations..."\n\
    ./efbundle --connection "$ConnectionStrings__DefaultConnection" || true\n\
fi\n\
exec dotnet AspendoraFileShare.dll\n\
' > /app/start.sh && chmod +x /app/start.sh

ENTRYPOINT ["/bin/sh", "/app/start.sh"]
