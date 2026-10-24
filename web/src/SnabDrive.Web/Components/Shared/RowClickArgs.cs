namespace SnabDrive.Web.Components.Shared;

/// <summary>Клик по строке реестра с модификаторами (для мультивыделения без галочек).</summary>
public sealed record RowClickArgs(Domain.RegistryRowDto Row, bool Ctrl, bool Shift);
