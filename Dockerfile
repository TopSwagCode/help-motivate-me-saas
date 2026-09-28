FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/HelpMotivateMe.SaaS.Api/HelpMotivateMe.SaaS.Api.csproj src/HelpMotivateMe.SaaS.Api/
RUN dotnet restore src/HelpMotivateMe.SaaS.Api/HelpMotivateMe.SaaS.Api.csproj
COPY src/HelpMotivateMe.SaaS.Api/ src/HelpMotivateMe.SaaS.Api/
RUN dotnet publish src/HelpMotivateMe.SaaS.Api/HelpMotivateMe.SaaS.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl \
	&& rm -rf /var/lib/apt/lists/* \
	&& mkdir -p /data \
	&& chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "HelpMotivateMe.SaaS.Api.dll"]