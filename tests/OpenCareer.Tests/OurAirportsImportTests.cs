using OpenCareer.Infrastructure.AviationData;

namespace OpenCareer.Tests;

public sealed class OurAirportsImportTests
{
    [Fact]
    public void Import_CreatesOfflineLookup_AndFailedRefreshKeepsPreviousData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencareer-airports-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Write(directory, "countries.csv", "id,code,name,continent\n1,US,United States,NA\n");
            Write(directory, "regions.csv", "id,code,local_code,name,continent,iso_country\n2,US-TX,TX,Texas,NA,US\n");
            Write(directory, "airports.csv", "id,ident,type,name,latitude_deg,longitude_deg,elevation_ft,continent,iso_country,iso_region,municipality,scheduled_service,gps_code,icao_code,iata_code,local_code\n3,KDFW,large_airport,\"Dallas, Fort Worth\",32.8998,-97.0403,607,NA,US,US-TX,Dallas,yes,KDFW,KDFW,DFW,\n");
            Write(directory, "runways.csv", "id,airport_ident,length_ft,width_ft,surface,lighted,closed,le_ident,le_heading_degT,le_displaced_threshold_ft,he_ident,he_heading_degT,he_displaced_threshold_ft\n4,KDFW,13401,200,CON,1,0,17L,175,,35R,355,\n");
            Write(directory, "airport-frequencies.csv", "id,airport_ident,type,description,frequency_mhz\n5,KDFW,TWR,Dallas tower,118.7\n");
            Write(directory, "navaids.csv", "id,ident,name,type,frequency_khz,latitude_deg,longitude_deg,iso_country,associated_airport\n6,DFW,Dallas,VOR-DME,117000,32.9,-97.0,US,KDFW\n");

            var databasePath = Path.Combine(directory, "reference.sqlite");
            var importer = new OurAirportsImportService();
            var counts = importer.Import(directory, databasePath);
            Assert.Equal(6, counts.Count);
            Assert.All(counts.Values, count => Assert.Equal(1, count));

            var reference = new AviationReferenceDatabase(databasePath);
            Assert.Equal("Dallas, Fort Worth", reference.FindAirport("dfw")?.Name);
            Assert.Equal(13401, Assert.Single(reference.GetRunways("KDFW")).LengthFeet);

            Write(directory, "runways.csv", "id,airport_ident,length_ft\n4,KDFW,1\n");
            Assert.Throws<InvalidDataException>(() => importer.Import(directory, databasePath));
            Assert.Equal("Dallas, Fort Worth", reference.FindAirport("KDFW")?.Name);
            Assert.Equal(13401, Assert.Single(reference.GetRunways("KDFW")).LengthFeet);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Write(string directory, string filename, string content) =>
        File.WriteAllText(Path.Combine(directory, filename), content);
}
