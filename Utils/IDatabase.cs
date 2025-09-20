namespace ALab_Cabinet.Utils;

public interface IDatabase
{
    void Start(bool isLocal = false);
    void End();
    IAsyncEnumerable<(Dictionary<string, object> Fields, string Id)> GetAllRecords(string tableName);
    IAsyncEnumerable<(Dictionary<string, object> Fields, string Id)> GetAllRecords<T>(string tableName, string nameField, T param);
    Task<(Dictionary<string, object> Fields, string Id)> GetRecord<T>(string tableName, string nameField, T field, bool isString = true, Match match = Match.Exact);
    Task<(Dictionary<string, object> Fields, string Id)> GetRecordById(string tableName, string id);
    Task<bool> Delete(string tableName, string id);
    Task<bool> Update(string tableName, string id, Dictionary<string, object> dct);
    Task<(bool Success, string Id)> Create(string tableName, Dictionary<string, object> dct);
    T? GetId<T>(object obj);
}