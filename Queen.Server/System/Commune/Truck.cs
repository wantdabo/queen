using MongoDB.Driver;
using Queen.Common.DB;
using System.Collections.Concurrent;
using TouchSocket.Core;

namespace Queen.Server.System.Commune;

/// <summary>
/// 数据保存事件
/// </summary>
public struct DBSaveReq
{
    /// <summary>
    /// 集合名称
    /// </summary>
    public string collection { get; set; }
    /// <summary>
    /// 标识
    /// </summary>
    public string token { get; set; }
    /// <summary>
    /// 数据
    /// </summary>
    public byte[] value { get; set; }
    /// <summary>
    /// 用户信息
    /// </summary>
    public DBRoleValue dbRoleValue { get; set; }
}

public class Truck : Sys
{
    /// <summary>
    /// 数据自动保存计时器 ID
    /// </summary>
    private uint dbsaveTiming;

    /// <summary>
    /// 脏数据请求
    /// </summary>
    private ConcurrentQueue<DBSaveReq> dbSaveEvents = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        dbsaveTiming = engine.ticker.Timing((t) =>
        {
            var events = dbSaveEvents.ToArray();
            dbSaveEvents.Clear();
            Task.Run(() =>
            {
                var rolesDict = new Dictionary<string, DBRoleValue>();
                var datasDict = new Dictionary<string, byte[]>();
                foreach (var saveEvent in events)
                {
                    switch (saveEvent.collection)
                    {
                        case "roles":
                            rolesDict.AddOrUpdate(saveEvent.dbRoleValue.username, saveEvent.dbRoleValue);
                            break;
                        case "datas":
                            datasDict.AddOrUpdate(saveEvent.token, saveEvent.value);
                            break;
                    }
                }
                if (rolesDict.Count > 0)
                {
                    var updateRequests = new List<WriteModel<DBRoleValue>>();
                    foreach (var kv in rolesDict)
                    {
                        updateRequests.Add(new UpdateOneModel<DBRoleValue>(
                            Builders<DBRoleValue>.Filter.Eq(r => r.uuid, kv.Value.uuid),
                            Builders<DBRoleValue>.Update.SetOnInsert(r => r, kv.Value)
                        ));
                    }
                    engine.dbo.UpdateMany("roles", updateRequests);
                }

                if (datasDict.Count > 0)
                {
                    var updateRequests1 = new List<WriteModel<DBDataValue>>();
                    foreach (var kv in datasDict)
                    {
                        updateRequests1.Add(new UpdateOneModel<DBDataValue>(
                            Builders<DBDataValue>.Filter.Eq(r => r.prefix, kv.Key),
                            Builders<DBDataValue>.Update.SetOnInsert(r => r.value, kv.Value)
                        )
                        {
                            IsUpsert = true
                        });
                    }
                    engine.dbo.UpdateMany("datas", updateRequests1);
                }
            });
        }, engine.settings.dbsave, -1);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        engine.ticker.StopTimer(dbsaveTiming);
    }

    public void Save(DBSaveReq e)
    {
        dbSaveEvents.Enqueue(e);
    }
}