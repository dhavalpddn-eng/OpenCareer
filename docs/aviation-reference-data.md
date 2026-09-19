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
