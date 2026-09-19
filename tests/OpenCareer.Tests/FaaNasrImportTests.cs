using System.IO.Compression;
using Microsoft.Data.Sqlite;
using OpenCareer.Infrastructure.AviationData;

namespace OpenCareer.Tests;

public sealed class FaaNasrImportTests
{
    [Fact]
    public void Import_NestedCsvArchive_PreservesPriorSnapshotOnInvalidCycle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencareer-faa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var archive = Path.Combine(directory, "nasr.zip");
            var database = Path.Combine(directory, "aviation.sqlite");
            CreateSubscription(archive, "2026/09/03");
            var importer = new FaaNasrImportService();
            var imported = importer.Import(archive, database);
            Assert.Equal(new DateOnly(2026, 9, 3), imported.EffectiveDate);
            Assert.Equal(8, imported.Counts.Count);

            var reference = new FaaNasrReferenceDatabase(database);
            var airport = Assert.IsType<FaaAirportReference>(reference.FindAirport("kdfw"));
            Assert.Equal("DFW", airport.FaaId);
            Assert.Equal(13_401, Assert.Single(reference.GetRunways(airport)).LengthFeet);
            Assert.Equal(2, reference.GetMilitaryRoutePoints("IR", "002").Count);

            CreateSubscription(archive, "2026/10/01", routePointDate: "2026/09/03");
            Assert.Throws<InvalidDataException>(() => importer.Import(archive, database));
            Assert.Equal(13_401, Assert.Single(reference.GetRunways(airport)).LengthFeet);
            using var connection = new SqliteConnection($"Data Source={database}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT effective_date FROM faa_import";
            Assert.Equal("2026-09-03", command.ExecuteScalar());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateSubscription(string path, string date, string? routePointDate = null)
    {
        if (File.Exists(path)) File.Delete(path);
        var files = new Dictionary<string, string>
        {
            ["APT_BASE.csv"] = $"EFF_DATE,SITE_NO,SITE_TYPE_CODE,ARPT_ID,ICAO_ID,ARPT_NAME,CITY,STATE_CODE,COUNTRY_CODE,LAT_DECIMAL,LONG_DECIMAL,ELEV,FACILITY_USE_CODE,MIL_LNDG_FLAG,JOINT_USE_FLAG,ARPT_STATUS\n{date},23710.6,A,DFW,KDFW,DALLAS-FORT WORTH INTL,DALLAS,TX,US,32.897,-97.038,607,PU,N,N,O\n",
            ["APT_RWY.csv"] = $"EFF_DATE,SITE_NO,SITE_TYPE_CODE,ARPT_ID,RWY_ID,RWY_LEN,RWY_WIDTH,SURFACE_TYPE_CODE,COND,RWY_LGT_CODE\n{date},23710.6,A,DFW,18L/36R,13401,200,CONC,G,HIRL\n",
            ["APT_RWY_END.csv"] = $"EFF_DATE,SITE_NO,SITE_TYPE_CODE,RWY_ID,RWY_END_ID,TRUE_ALIGNMENT,ILS_TYPE,LAT_DECIMAL,LONG_DECIMAL,DISPLACED_THR_LEN,TKOF_DIST_AVBL,LNDG_DIST_AVBL\n{date},23710.6,A,18L/36R,18L,180,,32.9,-97.0,,13401,13401\n",
            ["FIX_BASE.csv"] = $"EFF_DATE,FIX_ID,ICAO_REGION_CODE,STATE_CODE,COUNTRY_CODE,LAT_DECIMAL,LONG_DECIMAL,FIX_USE_CODE\n{date},TESTX,K1,TX,US,32.8,-97.0,\n",
            ["CLS_ARSP.csv"] = $"EFF_DATE,SITE_NO,SITE_TYPE_CODE,ARPT_ID,CLASS_B_AIRSPACE,CLASS_C_AIRSPACE,CLASS_D_AIRSPACE,CLASS_E_AIRSPACE,AIRSPACE_HRS,REMARK\n{date},23710.6,A,DFW,Y,,,,,\n",
            ["MIL_OPS.csv"] = $"EFF_DATE,SITE_NO,SITE_TYPE_CODE,ARPT_ID,MIL_OPS_OPER_CODE,MIL_OPS_CALL,MIL_OPS_HRS,REMARK\n{date},23710.6,A,DFW,R,TEST,24H,\n",
            ["MTR_BASE.csv"] = $"EFF_DATE,ROUTE_TYPE_CODE,ROUTE_ID,ARTCC,TIME_OF_USE\n{date},IR,002,ZFW,CONTINUOUS\n",
            ["MTR_PT.csv"] = $"EFF_DATE,ROUTE_TYPE_CODE,ROUTE_ID,ROUTE_PT_SEQ,ROUTE_PT_ID,NEXT_ROUTE_PT_ID,SEGMENT_TEXT,LAT_DECIMAL,LONG_DECIMAL\n{routePointDate ?? date},IR,002,10,A,B,LEG,32.0,-97.0\n{routePointDate ?? date},IR,002,20,B,C,LEG,33.0,-98.0\n"
        };
        using var outer = ZipFile.Open(path, ZipArchiveMode.Create);
        var nested = outer.CreateEntry("CSV_Data/03_Sep_2026_CSV.zip");
        using var output = nested.Open();
        using var inner = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (name, content) in files)
        {
            var entry = inner.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
