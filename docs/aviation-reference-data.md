# OurAirports reference data

OurAirports supplies the offline reference database for airports, runways, communication frequencies, navaids, countries and regions. The dataset is public domain and refreshed nightly. The source publishes no guarantee of accuracy or fitness for use; use this for gameplay and reference, not real flight navigation. It does not provide airline schedules or complete instrument procedures.
Airport type alone does not establish military access or mission eligibility.

The importer is an explicit development/update operation. Normal gameplay reads only the local SQLite database through `AviationReferenceDatabase`; it performs no downloads and needs no account or API key. Its `oa_` tables are separate from career saves. The app shell does not yet call the reader because dispatch and map features have not been built.

To download the current datasets and build the database:

```sh
dotnet run --project src/OpenCareer.DataImport/OpenCareer.DataImport.csproj -- refresh data/aviation.sqlite
```

To import previously downloaded CSV files without network access:

```sh
dotnet run --project src/OpenCareer.DataImport/OpenCareer.DataImport.csproj -- import data/aviation.sqlite path/to/ourairports-csv
```

The directory must contain `airports.csv`, `runways.csv`, `airport-frequencies.csv`, `navaids.csv`, `countries.csv` and `regions.csv` from [OurAirports](https://ourairports.com/data/). Put the resulting `aviation.sqlite` alongside the distributed app's data when packaging. Do not store API credentials or live flight data in this database.

An import replaces all six reference tables in one transaction. Missing or malformed files leave the previous reference data intact. The importer records its import timestamp in `oa_import`. A database produced by `refresh` can be bundled with releases; users do not need to refresh it. Compare runway and airport identity with what MSFS actually loads before relying on an airport for a job; data sources can disagree.

## OpenAIP airspace layer

[OpenAIP](https://www.openaip.net/) adds airspace outlines and vertical-limit/activation metadata in separate `oaip_airspaces` tables. A developer refreshes one country at a time using the [Core API](https://docs.openaip.net/) and an API key in the `OPENAIP_API_KEY` environment variable. The key is sent only as the `x-openaip-api-key` request header. Never commit it, include it in a release, log it, or ask players for one.

```sh
dotnet run --project src/OpenCareer.DataImport/OpenCareer.DataImport.csproj -- refresh-openaip data/aviation.sqlite US
```

The command downloads all pages for the requested country, checks the item count, and replaces that country's records atomically. It leaves other countries and OurAirports data alone. Normal gameplay only queries the local database through `OpenAipAirspaceDatabase.FindBoundsCandidates`; it makes no OpenAIP request. A bounding-box match is only a map candidate, not a flight-clearance decision. Activation can depend on NOTAMs, requests and operating hours. Never infer a current restriction or military authorization from the imported shapes alone.

OpenAIP's [data license and published commercial-use note](https://www.openaip.net/) require attribution and state that the data must remain free to use; OpenAIP says its data may accompany paid applications when the application is not exclusively selling the dataset. Display `OpenAipAirspaceDatabase.Attribution` anywhere the map uses this data, with a link to OpenAIP. OpenAIP source data must remain identifiable and separate from OurAirports public-domain data. The app has no map screen yet, so the data is not shipped or displayed in the player UI.
