using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

using MySqlConnector;
using System.Buffers.Text;
using System.Data;
using System.Data.Common;
using System.Transactions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Authentication;
using System.Security;
using System.Net.Http.Json;

namespace NearChatBackend
{
    [JsonSerializable(typeof(APIRoot))]
    [JsonSerializable(typeof(LocationData))]
    internal partial class GeoSerialisation : JsonSerializerContext
    {

    }

    class APIRoot
    {
        public LocationData address { get; set; }
    }
    public class LocationData
        {
        // House
        public string? house { get; set; }
        public string? building { get; set; }
        public string? public_building { get; set; }
        public string? isolated_dwelling { get; set; }
        public string? farmland { get; set; }
        public string? allotments { get; set; }

        // Road
        public string? road { get; set; }
        public string? footway { get; set; }
        public string? street { get; set; }
        public string? street_name { get; set; }
        public string? residential { get; set; }
        public string? path { get; set; }
        public string? pedestrian { get; set; }
        public string? road_reference { get; set; }
        public string? road_reference_intl { get; set; }
        public string? square { get; set; }

        // Place
        public string? place { get; set; }

        // Hamlet
        public string? hamlet { get; set; }
        public string? locality { get; set; }
        public string? croft { get; set; }

        // Village
        public string? village { get; set; }

        // Neighbourhood
        public string? neighbourhood { get; set; }
        public string? suburb { get; set; }
        public string? city_district { get; set; }
        public string? commune { get; set; }
        public string? district { get; set; }
        public string? quarter { get; set; }
        public string? borough { get; set; }
        public string? city_block { get; set; }
        public string? residential_area { get; set; }
        public string? commercial { get; set; }
        public string? industrial { get; set; }
        public string? houses { get; set; }
        public string? subdistrict { get; set; }
        public string? subdivision { get; set; }
        public string? ward { get; set; }

        // Postal City
        public string? postal_city { get; set; }

        // City
        public string? city { get; set; }
        public string? town { get; set; }
        public string? township { get; set; }

        // Municipality
        public string? municipality { get; set; }
        public string? local_administrative_area { get; set; }
        public string? subcounty { get; set; }

        // County
        public string? county { get; set; }
        public string? county_code { get; set; }
        public string? department { get; set; }

        // State District
        public string? state_district { get; set; }

        // Postcode
        public string? postcode { get; set; }
        public string? postal_code { get; set; }
        public string? partial_postcode { get; set; }

        // State
        public string? state { get; set; }
        public string? province { get; set; }
        public string? state_code { get; set; }
        public string? territory { get; set; }

        // Region
        public string? region { get; set; }
        public string? oblast { get; set; }
        public string? raion { get; set; }

        // Island
        public string? island { get; set; }

        // Archipelago
        public string? archipelago { get; set; }

        // Country
        public string? country { get; set; }
        public string? country_name { get; set; }

        // Country Code
        public string? country_code { get; set; }

        // Continent
        public string? continent { get; set; }

        // Ordered field list for AOT-safe iteration
        private static readonly List<Func<LocationData, string?>> OrderedFields = new List<Func<LocationData, string?>>
        {
        d => d.house, d => d.building, d => d.public_building, d => d.isolated_dwelling, d => d.farmland, d => d.allotments,
        d => d.road, d => d.footway, d => d.street, d => d.street_name, d => d.residential, d => d.path, d => d.pedestrian, d => d.road_reference,
        d => d.road_reference_intl, d => d.square, d => d.place, d => d.hamlet, d => d.locality, d => d.croft, d => d.village,
        d => d.neighbourhood, d => d.suburb, d => d.city_district, d => d.commune, d => d.district, d => d.quarter, d => d.borough, d => d.city_block,
        d => d.residential_area, d => d.commercial, d => d.industrial, d => d.houses, d => d.subdistrict, d => d.subdivision, d => d.ward,
        d => d.postal_city, d => d.city, d => d.town, d => d.township, d => d.municipality, d => d.local_administrative_area, d => d.subcounty,
        d => d.county, d => d.county_code, d => d.department, d => d.state_district, d => d.postcode, d => d.postal_code, d => d.partial_postcode,
        d => d.state, d => d.province, d => d.state_code, d => d.territory, d => d.region, d => d.oblast, d => d.raion, d => d.island, d => d.archipelago,
        d => d.country, d => d.country_name, d => d.country_code, d => d.continent
        };

        /// <summary>
        /// Returns the first non-null, non-empty field value based on priority order.
        /// </summary>
        public string? GetNthValidField(int n)
        {
            int count = 0;
            foreach (var fieldGetter in OrderedFields)
            {
                string? value = fieldGetter(this);
                if (!string.IsNullOrEmpty(value))
                {
                    count++;
                    if (count == n)
                    {
                        return value;
                    }
                }
            }
            return null;
        }

        public string? GetFirstValidField() => GetNthValidField(1);
        public string? GetSecondValidField() => GetNthValidField(2);
        public string? GetThirdValidField() => GetNthValidField(3);
        public string? GetFourthValidField() => GetNthValidField(4);
    }

    class Address
    {
        public required string very_local { get; set; }
        public string? neighbourhood { get; set; }
        public string? city { get; set; }
        public string? country { get; set; }
    }


    class ReverseGeocoding
    {

        const string API = "https://nominatim.openstreetmap.org/reverse?lat={0}&lon={1}&format=json";

        private static readonly HttpClient client;
        static ReverseGeocoding()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Nearchat-Project");
        }

        public static async Task<Address?> ReverseGeocode(double lat, double lon)
        {
            string URL = string.Format(API, lat, lon);

            HttpResponseMessage res = await client.GetAsync(URL);

            string json = await res.Content.ReadAsStringAsync();

            APIRoot? resp = JsonSerializer.Deserialize(json, GeoSerialisation.Default.APIRoot);


            if (resp?.address?.GetFirstValidField() == null)
            {
                return null;
            }


            return new Address { very_local = resp.address.GetFirstValidField(), neighbourhood = resp.address.GetSecondValidField(), city = resp.address.GetThirdValidField(), country = resp.address.GetFourthValidField() };
        }
    }
}
