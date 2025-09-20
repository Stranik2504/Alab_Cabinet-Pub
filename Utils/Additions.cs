using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json.Linq;

namespace ALab_Cabinet.Utils;

public static class Additions
{
    public static string EncodeUrl(this string text) => HttpUtility.UrlEncode(text);
    
    public static bool IsDefault<T>(this T obj) => EqualityComparer<T>.Default.Equals(obj, default);
    
    public static Dictionary<T, J> AddDict<T, J, K, L>(this Dictionary<T, J> dict, Dictionary<K, L> dictAdd, Func<K, T> keyConv, Func<L, J> valConv) where T : notnull where K : notnull
    {
        foreach (var pair in dictAdd)
        {
            dict.TryAdd(keyConv(pair.Key), valConv(pair.Value));
        }

        return dict;
    }
    
    public static T? To<T>(this object obj)
    {
        if (obj is T res)
            return res;

        return default;   
    }
    
    public static List<T>? ToList<T>(this JArray arr) => arr.ToObject<List<T>>();
    
    public static void ForEach<T>(this IEnumerable<T> en, Action<T> action)
    {
        if (action == null)
            return;
        
        foreach (var item in en)
        {
            action?.Invoke(item);
        }
    }
    
    public static int RemoveAll<TKey, TValue>(this Dictionary<TKey, TValue>? dictionary, Func<KeyValuePair<TKey, TValue>, bool>? predicate)
    {
        if (dictionary == null) return -1;
        if (predicate == null) return -1;

        var cnt = 0;

        for (var i = 0; i < dictionary.Count; i++)
        {
            var pair = dictionary.ElementAt(i);
            
            if (predicate(pair))
            {
                dictionary.Remove(pair.Key);
                i--;
                cnt++;
            }
        }

        return cnt;
    }
    
    public static T? Get<T, J>(this Dictionary<J, T> dct, J key) where J : notnull => dct.GetValueOrDefault(key);
    
    public static string GetString<T, J>(this Dictionary<T, J> dct, T key) where T : notnull => dct.GetValueOrDefault(key)?.ToString() ?? string.Empty;
    
    public static bool TryParseDateTime(this string? text, out DateTime date)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            date = default;
            return false;
        }
        
        var patterns = new[] { "yyyy.MM.ddThh:mm:ss", "yyyy-MM-ddThh:mm:ss", "dd.MM.yyyy", "yyyy-MM-dd" };
        
        foreach (var pattern in patterns)
        {
            if (text.Contains('T') && !pattern.Contains('T'))
                continue;
            
            if (text.Contains('T'))
            {
                var regStr = pattern
                    .Replace("hh", @"\d{1,2}")
                    .Replace("mm", @"\d{1,2}")
                    .Replace("ss", @"\d{1,2}")
                    .Replace("yyyy", @"\d{1,4}")
                    .Replace("MM", @"\d{1,2}")
                    .Replace("dd", @"\d{1,2}");
                
                if (!Regex.IsMatch(text, regStr))
                    continue;
                
                var dateMass = text.Split('T', '-', ':', '.');
                var modules = pattern.Split('T', '-', ':', '.');
                
                if (dateMass.Length != modules.Length)
                    continue;

                var year = DateTime.MinValue.Year;
                var month = DateTime.MinValue.Month;
                var day = DateTime.MinValue.Day;
                var hour = DateTime.MinValue.Hour;
                var minutes = DateTime.MinValue.Minute;
                var seconds = DateTime.MinValue.Second;

                for (var i = 0; i < modules.Length; i++)
                {
                    switch (modules[i])
                    {
                        case "yyyy":
                            year = int.Parse(dateMass[i]);
                            break;
                        case "MM":
                            month = int.Parse(dateMass[i]);
                            break;
                        case "dd":
                            day = int.Parse(dateMass[i]);
                            break;
                        case "hh":
                            hour = int.Parse(dateMass[i]);
                            break;
                        case "mm":
                            minutes = int.Parse(dateMass[i]);
                            break;
                        case "ss":
                            seconds = int.Parse(dateMass[i]);
                            break;
                    }
                }

                date = new DateTime(year, month, day, hour, minutes, seconds);
                return true;
            }
            
            if (DateTime.TryParseExact(text, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }

        date = default;
        return false;
    }
    
    public static DateTime ParseDateTime(this string text)
    {
        if (text.TryParseDateTime(out var date))
            return date;

        throw new ArgumentException("Invalid date format");
    }
}