using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;

namespace HealthixSFTP
{
    internal static class Program
    {
        // #63833 required-field rules. Shared by the registration query and the consent exception report so the two never drift.
        private const string RequiredFieldFilter = @"
                    C.LastName IS NOT NULL AND C.FirstName IS NOT NULL AND C.DOB IS NOT NULL
                    AND C.Address1 IS NOT NULL AND C.City IS NOT NULL AND C.[State] IS NOT NULL AND C.Zip IS NOT NULL
                    AND C.County IS NOT NULL AND (C.PhoneNumber IS NOT NULL OR C.CellNumber IS NOT NULL)";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static int Main()
        {
            // The tool has no window, so write any failure next to the exe instead of crashing silently.
            try
            {
                Run();
                return 0;
            }
            catch (Exception ex)
            {
                string errorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "HAMASPIKKC_Healthix_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_Error.txt");
                File.WriteAllText(errorPath, ex.ToString());
                return 1;
            }
        }

        private static void Run()
        {
            string todayTimestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string filePrefix = Path.Combine(baseDir, "HAMASPIKKC_Healthix_" + todayTimestamp);
            string outputPath = filePrefix + ".txt";
            string exceptionsPath = filePrefix + "_Exceptions.csv";
            string summaryPath = filePrefix + "_Summary.txt";
            string connStr = ConfigurationManager.ConnectionStrings["Hamaspik_LiveEntities"].ConnectionString;
            string consentPath = ConfigurationManager.AppSettings["ConsentFilePath"];
            if (!Path.IsPathRooted(consentPath))
                consentPath = Path.Combine(baseDir, consentPath);
            Dictionary<int, DateTime> consents = ConsentReport.Load(consentPath);

            List<Individual> records = new List<Individual>();
            List<string[]> exceptions = new List<string[]>();
            string database;

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                database = conn.DataSource + " / " + conn.Database;

                // Session temp table holding the consent list (ADR-2) - dropped automatically when the connection closes.
                using (SqlCommand cmd = new SqlCommand(
                    "CREATE TABLE #HealthixConsentedClients (ClientId INT NOT NULL PRIMARY KEY, ConsentDate DATE NOT NULL);", conn))
                {
                    cmd.ExecuteNonQuery();
                }

                DataTable consentTable = new DataTable();
                consentTable.Columns.Add("ClientId", typeof(int));
                consentTable.Columns.Add("ConsentDate", typeof(DateTime));
                foreach (var consent in consents)
                    consentTable.Rows.Add(consent.Key, consent.Value);

                using (SqlBulkCopy bulk = new SqlBulkCopy(conn) { DestinationTableName = "#HealthixConsentedClients" })
                {
                    bulk.WriteToServer(consentTable);
                }

                string sql = @"
                SELECT C.ClientId, C.LastName, C.FirstName, C.MiddleName, C.DOB, C.Gender, C.Address1, C.Address2, C.City, C.State, C.Zip, CT.Name AS County, C.PhoneNumber, C.CellNumber, C.SSN, C.MedicaidCIN,
                            HC.ConsentDate
                FROM        Services_Clients C
                LEFT JOIN   HipaaBilling_Counties CT ON CT.ID = C.County
                LEFT JOIN   #HealthixConsentedClients HC ON HC.ClientId = C.ClientId
                WHERE       C.IsActive=1 AND " + RequiredFieldFilter;

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var record = new Individual
                        {
                            LastName = reader["LastName"].ToString(),
                            FirstName = reader["FirstName"].ToString(),
                            MiddleName = reader["MiddleName"].ToString(),
                            ClientCode = reader["ClientId"].ToString(),
                            DOB = Individual.FormatDate(reader["DOB"].ToString()),
                            Gender = Individual.FormatGender(reader["Gender"].ToString()),
                            Address1 = reader["Address1"].ToString(),
                            Address2 = reader["Address2"].ToString(),
                            City = reader["City"].ToString(),
                            State = reader["State"].ToString(),
                            Zip = reader["Zip"].ToString(),
                            County = reader["County"].ToString(),
                            PhoneNumber = reader["PhoneNumber"].ToString(),
                            CellNumber = reader["CellNumber"].ToString(),
                            SSN = reader["SSN"].ToString(),
                            MedicaidCIN = reader["MedicaidCIN"].ToString(),
                            // Consent = Y only for clients on the Sipuk consent list; everyone else stays blank (Epic 82734)
                            ConsentEffectiveDate = Individual.FormatDate(reader["ConsentDate"])
                        };
                        record.Consent = string.IsNullOrEmpty(record.ConsentEffectiveDate) ? "" : "Y";
                        // Now apply format
                        record.PhoneNumber = Individual.FormatPhone(record.PhoneNumber, record.CellNumber);
                        records.Add(record);
                    }
                }

                // Every consent-list entry that will not reach Healthix, with the reason.
                string exceptionSql = @"
                SELECT  HC.ClientId,
                        CASE WHEN C.ClientId IS NULL THEN 'Client identifier not found in Enterprise'
                             WHEN ISNULL(C.IsActive, 0) <> 1 THEN 'Found, but excluded from the registration population (client is not active)'
                             ELSE 'Found and in the population, but dropped by the required-field filter' END AS Reason,
                        CASE WHEN C.ClientId IS NOT NULL AND C.IsActive = 1 THEN CONCAT(
                             CASE WHEN C.LastName IS NULL THEN 'LastName; ' END,
                             CASE WHEN C.FirstName IS NULL THEN 'FirstName; ' END,
                             CASE WHEN C.DOB IS NULL THEN 'DOB; ' END,
                             CASE WHEN C.Address1 IS NULL THEN 'Address1; ' END,
                             CASE WHEN C.City IS NULL THEN 'City; ' END,
                             CASE WHEN C.[State] IS NULL THEN 'State; ' END,
                             CASE WHEN C.Zip IS NULL THEN 'Zip; ' END,
                             CASE WHEN C.County IS NULL THEN 'County; ' END,
                             CASE WHEN C.PhoneNumber IS NULL AND C.CellNumber IS NULL THEN 'PhoneNumber/CellNumber; ' END) END AS MissingFields
                FROM    #HealthixConsentedClients HC
                LEFT JOIN Services_Clients C ON C.ClientId = HC.ClientId
                WHERE   C.ClientId IS NULL OR ISNULL(C.IsActive, 0) <> 1 OR NOT (" + RequiredFieldFilter + @")
                ORDER BY HC.ClientId";

                using (SqlCommand cmd = new SqlCommand(exceptionSql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        exceptions.Add(new[]
                        {
                            reader["ClientId"].ToString(),
                            reader["Reason"].ToString(),
                            reader["MissingFields"].ToString().Trim().TrimEnd(';')
                        });
                    }
                }
            }

            // Convert objects → pipe-delimited lines
            List<string> lines = new List<string>();
            foreach (var r in records)
            {
                lines.Add(r.ToPipeDelimited());
            }

            File.WriteAllLines(outputPath, lines);

            List<string> exceptionLines = new List<string> { "ClientId,Reason,MissingFields" };
            exceptionLines.AddRange(exceptions.Select(e => string.Join(",", e.Select(CsvQuote))));
            File.WriteAllLines(exceptionsPath, exceptionLines);

            int consentedInFile = records.Count(r => r.Consent == "Y");
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Healthix bulk registration - " + todayTimestamp);
            summary.AppendLine("Database: " + database);
            summary.AppendLine("Registration rows written: " + records.Count);
            summary.AppendLine("Consent list entries loaded: " + consents.Count);
            summary.AppendLine("Consented clients in file (Consent = Y): " + consentedInFile);
            summary.AppendLine("Consent entries not reaching Healthix (see exceptions file): " + exceptions.Count);
            foreach (var group in exceptions.GroupBy(e => e[1]))
                summary.AppendLine("  " + group.Key + ": " + group.Count());
            summary.AppendLine(consentedInFile + exceptions.Count == consents.Count
                ? "Reconciliation OK: consented in file + exceptions = consent list"
                : "WARNING: consented in file + exceptions does not equal the consent list - investigate before submitting");
            File.WriteAllText(summaryPath, summary.ToString());

            Console.WriteLine(@"File generated: " + Path.GetFullPath(outputPath));
        }

        private static string CsvQuote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
