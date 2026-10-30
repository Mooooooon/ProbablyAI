using System;
using System.Collections.Generic;

namespace ProbablyAI.Hands
{
    public sealed class ContainerInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string ParentId { get; set; }
        public string OwnerItemId { get; set; }
        public string Kind { get; set; }
        public string Role { get; set; }
        public bool Locked { get; set; }
        public bool InsertLocked { get; set; }
        public bool RemoveLocked { get; set; }
        public bool IsOpen { get; set; }
        public int ItemCount { get; set; }
        public List<string> Children { get; set; } = new List<string>();
    }

    public sealed class ItemInfo
    {
        public string Id { get; set; }
        public string Identifier { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string ContainerId { get; set; }
        public string ContentContainerId { get; set; }
        public int Quantity { get; set; }
        public bool Owned { get; set; }
        public bool Removable { get; set; }
        public string Description { get; set; }
        public List<string> Types { get; set; } = new List<string>();
    }

    public sealed class WorldSnapshot
    {
        public List<ContainerInfo> Containers { get; set; } = new List<ContainerInfo>();
        public List<ItemInfo> Items { get; set; } = new List<ItemInfo>();
        public bool Truncated { get; set; }
    }

    public sealed class MoveCheck
    {
        public bool Allowed { get; set; }
        public string Reason { get; set; }
        public int FirstSlotQuantity { get; set; }
    }

    public sealed class MoveOutcome
    {
        public int Moved { get; set; }
        public string Detail { get; set; }
    }

    public interface IHandsBackend
    {
        WorldSnapshot Capture();
        MoveCheck CheckMove(string itemId, string destinationId, int quantity);
        MoveOutcome Move(string itemId, string destinationId, int quantity);
        void SetOpen(string containerId, bool open);
    }

    public sealed class HandsException : Exception
    {
        public string Code { get; }
        public HandsException(string code, string message) : base(message) => Code = code;
    }

    public sealed class HandsResult
    {
        public bool Success { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
        public static HandsResult Ok(object data, string message = "查询成功。")
            => new HandsResult { Success = true, Code = "ok", Message = message, Data = data };
        public static HandsResult Fail(string code, string message)
            => new HandsResult { Success = false, Code = code, Message = message };
    }
}
