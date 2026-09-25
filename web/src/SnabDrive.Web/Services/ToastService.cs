using Microsoft.AspNetCore.Components;

namespace SnabDrive.Web.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record ToastMessage(Guid Id, string Text, ToastKind Kind, DateTimeOffset CreatedAt);

/// <summary>Всплывающие уведомления в UI. Scoped — у каждого пользователя свой список.</summary>
public interface IToastService
{
    event Action? Changed;

    IReadOnlyList<ToastMessage> Messages { get; }

    void Show(string text, ToastKind kind = ToastKind.Info);

    void Dismiss(Guid id);
}

public sealed class ToastService : IToastService
{
    private const int MaxMessages = 5;

    private readonly List<ToastMessage> _messages = new();

    public event Action? Changed;

    public IReadOnlyList<ToastMessage> Messages => _messages;

    public void Show(string text, ToastKind kind = ToastKind.Info)
    {
        _messages.Insert(0, new ToastMessage(Guid.NewGuid(), text, kind, DateTimeOffset.Now));

        if (_messages.Count > MaxMessages)
        {
            _messages.RemoveRange(MaxMessages, _messages.Count - MaxMessages);
        }

        Changed?.Invoke();
    }

    public void Dismiss(Guid id)
    {
        if (_messages.RemoveAll(m => m.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }
}
