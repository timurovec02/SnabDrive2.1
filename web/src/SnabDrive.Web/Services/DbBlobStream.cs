using System.Data.Common;

namespace SnabDrive.Web.Services;

/// <summary>
/// Отдаёт содержимое вложения прямо из базы, не загружая файл в память процесса.
/// Держит читатель, команду и контекст (которому принадлежит соединение) открытыми,
/// пока клиент качает файл, и освобождает их в правильном порядке при закрытии потока.
/// Читает BLOB через ADO-читатель последовательно, поэтому провайдер не важен.
/// </summary>
public sealed class DbBlobStream : Stream
{
    private readonly Stream _inner;
    private readonly DbDataReader _reader;
    private readonly DbCommand? _command;
    private readonly IAsyncDisposable? _owner;

    public DbBlobStream(Stream inner, DbDataReader reader, DbCommand? command = null, IAsyncDisposable? owner = null)
    {
        _inner = inner;
        _reader = reader;
        _command = command;
        _owner = owner;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException("Длина потока из базы заранее неизвестна.");

    public override long Position
    {
        get => throw new NotSupportedException("Поток из базы доступен только для последовательного чтения.");
        set => throw new NotSupportedException("Поток из базы доступен только для последовательного чтения.");
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Порядок важен: сначала читатель, потом команда, потом контекст с соединением.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _reader.Dispose();
            _command?.Dispose();
            // IAsyncDisposable сам по себе Dispose не имеет — контекст реализует оба.
            (_owner as IDisposable)?.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await _reader.DisposeAsync();
        if (_command is not null)
        {
            await _command.DisposeAsync();
        }

        if (_owner is not null)
        {
            await _owner.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
