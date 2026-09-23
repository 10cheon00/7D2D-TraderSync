// Small game API doubles: these tests exercise production routing/packet code, not Unity.
using System;
using System.Collections.Generic;
using System.IO;

public enum NetPackageDirection { ToClient }
public abstract class NetPackage
{
    public virtual NetPackageDirection PackageDirection => NetPackageDirection.ToClient;
    public virtual void write(PooledBinaryWriter writer) => writer.Write((ushort)42);
    public abstract void read(PooledBinaryReader reader);
    public abstract void ProcessPackage(World world, GameManager callbacks);
    public abstract int GetLength();
}
public class PooledBinaryWriter : BinaryWriter { public PooledBinaryWriter(Stream s) : base(s) {} }
public class PooledBinaryReader : BinaryReader { public PooledBinaryReader(Stream s) : base(s) {} }
public static class NetPackageManager { public static T GetPackage<T>() where T : new() => new T(); }
public class Entity { public int entityId; }
public class EntityPlayer : Entity {}
public class EntityPlayerLocal : EntityPlayer {}
public class EntityTrader : Entity { public TraderData TraderData = new TraderData(); }
public class TraderData
{
    public int Count;
    public void Write(BinaryWriter writer) => writer.Write(Count);
    public void Read(BinaryReader reader) => Count = reader.ReadInt32();
    public void CopyFrom(TraderData other) => Count = other.Count;
}
public class World
{
    public readonly Dictionary<int, Entity> Entities = new Dictionary<int, Entity>();
    public EntityPlayerLocal LocalPlayer;
    public Entity GetEntity(int id) => Entities.TryGetValue(id, out var entity) ? entity : null;
    public EntityPlayerLocal GetPrimaryPlayer() => LocalPlayer;
}
public class GameManager { public static GameManager Instance = new GameManager(); public World World; }
public class SingletonMonoBehaviour<T> where T : new() { public static T Instance = new T(); }
public class ConnectionManager
{
    public bool IsServer;
    public readonly List<(int Recipient, NetPackage Packet)> Sent = new List<(int, NetPackage)>();
    public void SendPackage(NetPackage packet, bool _onlyClientsAttachedToAnEntity, int _attachedToEntityId)
        => Sent.Add((_attachedToEntityId, packet));
}
public class LocalPlayerUI
{
    public static LocalPlayerUI Current;
    public static LocalPlayerUI GetUIForPlayer(EntityPlayerLocal player) => Current;
    public WindowManager windowManager = new WindowManager();
    public XUi xui = new XUi();
}
public class WindowManager { public bool Open = true; public bool IsWindowOpen(string name) => Open; }
public class XUi { public TraderModel Trader = new TraderModel(); }
public class TraderModel { public EntityTrader Trader; public TraderGroup TraderWindowGroup = new TraderGroup(); }
public class TraderGroup
{
    public XUiC_TraderWindow Window = new XUiC_TraderWindow();
    public int BindingRefreshes;
    public T GetChildByType<T>() where T : class => Window as T;
    public void RefreshTraderWindow() => BindingRefreshes++;
}
public class XUiC_TraderWindow
{
    public int Refreshes;
    public XUiC_TraderItemList Items = new XUiC_TraderItemList();
    public T GetChildByType<T>() where T : class => Items as T;
    public void RefreshTraderItems() => Refreshes++;
}
public class XUiC_TraderItemList { public int Clears; public void ClearSelection() => Clears++; }

// UI behavior itself is exercised with the production helper by SelectionTests.
namespace TraderSync
{
    internal static class TraderSelectionRefresh
    {
        internal static void Refresh(XUiC_TraderWindow window) => window.RefreshTraderItems();
    }
}
