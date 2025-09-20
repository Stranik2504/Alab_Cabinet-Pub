using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ALab_Cabinet.Utils;

public class NocoDb : IDatabase
{
    // Const start url
    private const ushort MaxCountRowsInRequest = 100;
    
    // Main params
    private readonly string _token;
    private readonly string _nameDb;
    private readonly string _startUrl;
    private readonly bool _useFix;
    private readonly ILogger _logger;

    private string EncodeNameDb => _nameDb.EncodeUrl(); 
    
    // Client to set/get data from db
    private HttpClient? _client;

    public NocoDb(string startUrl, string token, string nameDb, ILogger<NocoDb> logger, bool useAirtableFix = true) => 
        (_startUrl, _token, _nameDb, _logger, _useFix) = (startUrl, token, nameDb, logger, useAirtableFix);
    
    public void Start(bool isLocal = false)
    {
        _logger.LogInformation("Start NocoDb");
        _client?.Dispose();

        _client = new HttpClient();
        
        // Set headers to client
        _client.DefaultRequestHeaders.Add("Accept", "application/json");
        _client.DefaultRequestHeaders.Add("xc-token", _token);
        _logger.LogInformation("End initialize NocoDb");
    }

    public void End()
    {
        _logger.LogInformation("End NocoDb");
        _client?.Dispose();
        _logger.LogInformation("End dispose NocoDb");
    }

    public async IAsyncEnumerable<(Dictionary<string, object> Fields, string Id)> GetAllRecords(string tableName)
    {
        _logger.LogInformation("Start All Records");

        if (_client == default)
        {
            _logger.LogError("Client is null. Break");
            yield break;
        }

        var i = 0;
        bool lastPage;

        do
        {
            _logger.LogInformation("Start Send request to get {0} count records with offset {0}", MaxCountRowsInRequest, i.ToString());
            
            var response = await GetRequest(
                _client,
                _startUrl + "data/v1/" + _nameDb + "/" + tableName,
                new Dictionary<string, string>()
                {
                    { "limit", MaxCountRowsInRequest.ToString() },
                    { "offset", i.ToString() },
                }
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Response is not success. Break");
                yield break;
            }
            _logger.LogInformation("Response is success. Try parse record from json");
            
            var listRecords = JsonConvert.DeserializeObject<ListRecords>(await response.Content.ReadAsStringAsync());

            if (listRecords == default || listRecords.List.Any(x => !x.ContainsKey(GetNameId(_useFix))))
            {
                _logger.LogError("List records is null or not contains Id. Break");
                yield break;
            }

            _logger.LogInformation("Start update and return records");
            
            foreach (var item in listRecords.List)
            {
                var record = RemoveEmptyFields(item);
                
                if (record == default)
                    continue;
                
                record.TryAdd("Id", record[GetNameId(_useFix)]);
                await FixIdLinks(_client, tableName, record);
                yield return (record, record["Id"].ToString() ?? string.Empty);
            }
            
            _logger.LogInformation("End update and return records");

            i += listRecords.PageInfo.PageSize;
            lastPage = listRecords.PageInfo.IsLastPage;
            
            _logger.LogInformation("Change offset to {0}", i.ToString());
        } while (!lastPage);
        
        _logger.LogInformation("End All Records");
    }

    public async IAsyncEnumerable<(Dictionary<string, object> Fields, string Id)> GetAllRecords<T>(string tableName, string nameField, T param)
    {
        _logger.LogInformation("Start All Records by field");

        if (_client == default)
        {
            _logger.LogError("Client is null. Break");
            yield break;
        }

        var i = 0;
        bool lastPage;

        if (nameField == "Id")
            nameField = GetNameId(_useFix);

        do
        {
            var response = await GetRequest(
                _client,
                _startUrl + "data/v1/" + _nameDb + "/" + tableName,
                new Dictionary<string, string>()
                {
                    { "limit", MaxCountRowsInRequest.ToString() },
                    { "offset", i.ToString() },
                    { "where", param.IsDefault() || (param is string val && string.IsNullOrWhiteSpace(val)) ? $"({nameField},is,null)" : $"({nameField},eq,{param})" }
                }
            );
            
            if (!response.IsSuccessStatusCode)
                yield break;
            
            var listRecords = JsonConvert.DeserializeObject<ListRecords>(await response.Content.ReadAsStringAsync());
            
            if (listRecords == default || listRecords.List.Any(x => !x.ContainsKey(GetNameId(_useFix))))
                yield break;

            foreach (var item in listRecords.List)
            {
                var record = RemoveEmptyFields(item);
                
                if (record == default)
                    continue;
                
                record.TryAdd("Id", record[GetNameId(_useFix)]);
                await FixIdLinks(_client, tableName, record);
                yield return (record, record["Id"].ToString() ?? string.Empty);
            }

            i += listRecords.PageInfo.PageSize;
            lastPage = listRecords.PageInfo.IsLastPage;
        } while (!lastPage);
    }

    public async Task<(Dictionary<string, object> Fields, string Id)> GetRecord<T>(string tableName, string nameField, T field, bool isString = true, Match match = Match.Exact)
    {
        if (_client == default)
            return (new Dictionary<string, object>(), string.Empty);

        string where;

        if (nameField == "Id")
            nameField = GetNameId(_useFix);

        switch (match)
        {
            case Match.Exact:
                where = $"({nameField},eq,{field})";
                break;
            case Match.Partial:
                where = $"({nameField},like,{field})";
                break;
            case Match.None:
            default:
                return await GetRecordInAll(tableName, nameField, field);
        }

        if (field.IsDefault() || (field is string val && string.IsNullOrWhiteSpace(val)))
            where = $"({nameField},is,null)";
        
        var response = await GetRequest(
            _client,
            _startUrl + "data/v1/" + _nameDb + "/" + tableName + "/find-one",
            new Dictionary<string, string>()
            {
                { "where", where }
            }
        );

        if (!response.IsSuccessStatusCode)
            return (new Dictionary<string, object>(), string.Empty);

        var gotRecord = JsonConvert.DeserializeObject<Dictionary<string, object?>>(await response.Content.ReadAsStringAsync());
        var record = RemoveEmptyFields(gotRecord);

        if (record == default || !record.ContainsKey(GetNameId(_useFix)))
            return (new Dictionary<string, object>(), string.Empty);
        
        await FixIdLinks(_client, tableName, record);
        
        record.TryAdd("Id", record[GetNameId(_useFix)]);

        return (record, record["Id"].ToString() ?? string.Empty);
    }

    public async Task<(Dictionary<string, object> Fields, string Id)> GetRecordById(string tableName, string id)
    {
        if (_client == default)
            return (new Dictionary<string, object>(), string.Empty);
        
        var response = await GetRequest(
            _client,
            _startUrl + "data/v1/" + _nameDb + "/" + tableName + "/find-one",
            new Dictionary<string, string>()
            {
                { "where", $"(ncRecordId,eq,{id})" }
            }
        );
        
        if (!response.IsSuccessStatusCode)
            return (new Dictionary<string, object>(), string.Empty);

        var gotRecord = JsonConvert.DeserializeObject<Dictionary<string, object?>>(await response.Content.ReadAsStringAsync());
        var record = RemoveEmptyFields(gotRecord);

        if (record == default || !record.ContainsKey(GetNameId(_useFix)))
            return (new Dictionary<string, object>(), string.Empty);
        
        await FixIdLinks(_client, tableName, record);
        
        record.TryAdd("Id", record[GetNameId(_useFix)]);

        return (record, record["Id"]?.ToString() ?? string.Empty);
    }

    public async Task<bool> Delete(string tableName, string id)
    {
        if (_client == default)
            return false;

        var response = await DeleteRequest(
            _client,
            _startUrl + "data/v1/" + _nameDb + "/" + tableName,
            id
        );

        if (!response.IsSuccessStatusCode)
            return false;
        
        var countDelete = JsonConvert.DeserializeObject<int>(await response.Content.ReadAsStringAsync());

        return countDelete > 0;
    }

    public async Task<bool> Update(string tableName, string id, Dictionary<string, object> dct)
    {
        if (_client == default)
            return false;
        
        if (dct.TryGetValue(GetNameId(_useFix), out var value) && dct.TryAdd("Id", value))
            dct.Remove(GetNameId(_useFix));

        var response = await PatchRequest(
            _client,
            _startUrl + "data/v1/" + _nameDb + "/" + tableName,
            new Dictionary<string, string>()
            {
                { GetNameId(_useFix), id }
            }.AddDict(dct, x => x, x => x.ToString() ?? string.Empty)
        );

        if (!response.IsSuccessStatusCode)
            return false;
        
        var record = JsonConvert.DeserializeObject<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());

        return record != default && record.ContainsKey(GetNameId(_useFix)) && record[GetNameId(_useFix)].ToString() == id;
    }

    public async Task<(bool Success, string Id)> Create(string tableName, Dictionary<string, object> dct)
    {
        if (_client == default)
            return (false, string.Empty);

        if (dct.TryGetValue("Id", out var value) && dct.TryAdd(GetNameId(_useFix), value))
            dct.Remove("Id");
        
        var response = await PostRequest(
            _client,
            _startUrl + "data/v1/" + _nameDb + "/" + tableName,
            dct.ToDictionary(x => x.Key, x => x.Value.ToString() ?? string.Empty)
        );

        if (!response.IsSuccessStatusCode)
            return (false, string.Empty);
        
        var record = JsonConvert.DeserializeObject<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());

        if (record == default || !record.ContainsKey(GetNameId(_useFix)))
            return (false, string.Empty);
        
        record.TryAdd("Id", record[GetNameId(_useFix)]);
        
        return (true, record["Id"].ToString() ?? string.Empty);
    }

    public T? GetId<T>(object obj)
    {
        if (obj is List<dynamic> list)
            return (T)Convert.ChangeType(list.FirstOrDefault(), typeof(T));
        
        return obj.To<T>();
    }

    public async Task<string> GetIdDb(string tableName)
    {
        if (_client == default)
            return string.Empty;
        
        var response = await _client.GetAsync($"{_startUrl}meta/projects/{_nameDb}/tables");
        
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        var tables = JsonConvert.DeserializeObject<Dictionary<string, List<dynamic>>>(await response.Content.ReadAsStringAsync());
        
        if (tables == default)
            return string.Empty;
        
        return tables["list"].First(x => x["title"] == tableName)["id"];
    }

    private string GetNameId(bool useNocoDbFix) => useNocoDbFix ? "ncRecordId" : "Id";
    
    private async Task<(Dictionary<string, object> Fields, string Id)> GetRecordInAll<T>(string tableName, string nameField, T field)
    {
        await foreach (var item in GetAllRecords(tableName))
        {
            if (item.Fields.TryGetValue(nameField, out var value) && value.ToString() == field?.ToString())
                return item;
        }

        return (new Dictionary<string, object>(), string.Empty);
    }

    private Task<HttpResponseMessage> GetRequest(HttpClient client, string url, Dictionary<string, string>? dictParams = default)
    {
        var requestUrl = url;

        if (dictParams == null) 
            return client.GetAsync(requestUrl);
        
        if (dictParams.Count > 0)
            requestUrl += "?";

        for (var i = 0; i < dictParams.Count; i++)
        {
            requestUrl += dictParams.Keys.ToList()[i] + "=" + dictParams.Values.ToList()[i];

            if (i + 1 < dictParams.Count)
                requestUrl += "&";
        }

        return client.GetAsync(requestUrl);
    }

    private Task<HttpResponseMessage> DeleteRequest(HttpClient client, string url, string? param = default) 
        => client.DeleteAsync(url + "/" + param);

    private Task<HttpResponseMessage> PatchRequest(HttpClient client, string url, IReadOnlyDictionary<string, string>? dictParams = default)
    {
        if (dictParams == default || !dictParams.TryGetValue(GetNameId(_useFix), out var id))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotAcceptable));

        var requestUrl = url + "/" + id;
        
        var serializedJsonDocument = JsonConvert.SerializeObject(dictParams);
        var stringUser = new StringContent(serializedJsonDocument, Encoding.UTF8, "application/json");

        return client.PatchAsync(requestUrl, stringUser);
    }
    
    private Task<HttpResponseMessage> PostRequest(HttpClient client, string url, IReadOnlyDictionary<string, string>? dictParams = default)
    {
        if (dictParams == default)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotAcceptable));

        var serializedJsonDocument = JsonConvert.SerializeObject(dictParams);
        var stringUser = new StringContent(serializedJsonDocument, Encoding.UTF8, "application/json");

        return client.PostAsync(url, stringUser);
    }

    private async Task FixIdLinks(HttpClient client, string tableName, Dictionary<string, object>? fields)
    {
        if (fields == default)
            return;

        var response = await client.GetAsync($"{_startUrl}meta/projects/{_nameDb}/tables");
        
        if (!response.IsSuccessStatusCode)
            return;

        var tables = JsonConvert.DeserializeObject<Dictionary<string, List<dynamic>>>(await response.Content.ReadAsStringAsync());
        
        if (tables == default)
            return;
        
        var currTableId = tables["list"].First(x => x["title"] == tableName)["id"];
        
        response = await client.GetAsync($"{_startUrl}meta/tables/" + currTableId);
        
        if (!response.IsSuccessStatusCode)
            return;
        
        var tableInfo = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(await response.Content.ReadAsStringAsync());

        if (tableInfo == default)
            return;

        var columns = (tableInfo["columns"] as JArray)?.ToList<Dictionary<string, dynamic>>();

        if (columns == default || columns.Count == 0)
            return;

        var linkColumns = columns.Where(x => x["uidt"] == "Links");

        foreach (var linkColumn in linkColumns)
        {
            var modelId = linkColumn["colOptions"]["fk_mm_model_id"];
            var idLinkTable = linkColumn["colOptions"]["fk_related_model_id"];
            
            if (modelId == null)
                return;
            
            var linkId = columns.First(x => 
                x["uidt"] == "LinkToAnotherRecord" && 
                x["colOptions"]["fk_related_model_id"] == modelId
            )["title"];
            
            if (linkId == null)
                return;

            if (!fields.ContainsKey(linkId))
                return;
            
            if (fields.ContainsKey(linkColumn["title"]))
                fields.Remove(linkColumn["title"]);

            var listDictParams = (fields[linkId] as JArray)?.ToList<Dictionary<string, dynamic>>();
            
            if (listDictParams == default || listDictParams.Count == 0)
                continue;
            
            dynamic? recordId = default;

            if (listDictParams.Count > 1)
            {
                recordId = new List<dynamic?>();
                
                foreach (var dictParams in listDictParams)
                {
                    var recordId1 = dictParams["table1_id"];
                    var recordId2 = dictParams["table2_id"];

                    if (recordId1.ToString() == fields["ncRecordId"].ToString())
                        recordId.Add(recordId2);
                    else
                        recordId.Add(recordId1);
                }
            }
            else
            {
                var recordId1 = listDictParams[0]["table1_id"];
                var recordId2 = listDictParams[0]["table2_id"];

                if (recordId1.ToString() == fields["ncRecordId"].ToString())
                    recordId = recordId2;
                else
                    recordId = recordId1;
            }
            
            fields.Remove(linkId);
            fields.Add(linkColumn["title"], recordId);
        }
    }

    private static Dictionary<string, object>? RemoveEmptyFields(Dictionary<string, object?>? fields)
    {
        if (fields == default)
            return default;

        var dict = new Dictionary<string, object>();

        foreach (var (key, value) in fields)
            if (value != default)
                dict.Add(key, value);

        return dict;
    }
}

public record ListRecords(
    List<Dictionary<string, object>> List,
    PageInfo PageInfo
);

public record PageInfo(
    int TotalRows,
    int Page,
    int PageSize,
    bool IsFirstPage,
    bool IsLastPage
);