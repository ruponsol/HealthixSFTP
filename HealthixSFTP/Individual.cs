using System;
using System.Collections.Generic;
using System.Linq;

namespace HealthixSFTP
{
    public class Individual
    {
        private static readonly HashSet<string> AllowedGenders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "M", "F", "O", "U", "A", "N" };
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string ClientCode { get; set; }
        public string MedicaidCIN { get; set; }
        public string SSN { get; set; }
        public string DOB { get; set; }
        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string _gender { get; set; }
        public string PhoneNumber { get; set; }
        public string CellNumber { get; set; }
        public string County { get; set; }
        public string MiddleName { get; set; }
        public DateTime? DateOfDeath { get; set; }
        // "Y" for clients on the Sipuk consent list, blank otherwise (Epic 82734)
        public string Consent { get; set; }
        // Consent signed date, YYYYMMDD; blank when Consent is blank
        public string ConsentEffectiveDate { get; set; }

        // GENDER PROPERTY WITH VALIDATION
        public string Gender
        {
            get => _gender;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _gender = "U";   // Default unknown
                    return;
                }

                // Normalize value
                string g = value.Trim().ToUpper();

                _gender = AllowedGenders.Contains(g) ? g : "U";
                // If invalid → assign Unknown (U)
            }
        }

        // Safe formatter
        public static string FormatDate(object value)
        {
            if (value == null || value == DBNull.Value)
                return "";

            DateTime dt;
            if (DateTime.TryParse(value.ToString(), out dt))
                return dt.ToString("yyyyMMdd");

            return ""; // invalid date case
        }
        public string ToPipeDelimited()
        {
            string todayTimestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            return string.Join("|", new string[]
            {
                "HAMASPIKKC", ClientCode, LastName, FirstName, MiddleName,"", DOB, Gender, Address1,
                Address2, City, State, Zip, "USA", County, PhoneNumber,"", SSN, Consent ?? "",todayTimestamp,
                MedicaidCIN,"","","","","", ConsentEffectiveDate ?? ""
            }.Select(CleanField));
        }

        // A line break or pipe inside a value would split the record or shift every later field.
        private static string CleanField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
        }
        public static string FormatPhone(object phoneValue, object cellValue)
        {
            string phone = phoneValue?.ToString()?.Trim();
            string cell = cellValue?.ToString()?.Trim();

            // Use phone if available, otherwise cell
            string raw = !string.IsNullOrWhiteSpace(phone) ? phone :
                !string.IsNullOrWhiteSpace(cell) ? cell : "";

            if (string.IsNullOrWhiteSpace(raw))
                return "";

            // Extract digits only
            string digits = new string(raw.Where(char.IsDigit).ToArray());

            // Must be 10 digits to format properly
            if (digits.Length != 10)
                return ""; // or keep raw if you want

            // Build format (555)555-5555
            return $"({digits.Substring(0, 3)}){digits.Substring(3, 3)}-{digits.Substring(6, 4)}";
        }
        public static string FormatGender(string genderText)
        {
            if (string.IsNullOrWhiteSpace(genderText))
                return "U";   // default Unknown

            genderText = genderText.Trim().ToLower();

            return genderText switch
            {
                "male" => "M",
                "m" => "M",
                "female" => "F",
                "f" => "F",

                "other" => "O",
                "o" => "O",

                "unknown" => "U",
                "u" => "U",

                "ambiguous" => "A",
                "a" => "A",

                "not applicable" => "N",
                "n" => "N",

                _ => "U" // default if nothing matches
            };
        }

    }
}
