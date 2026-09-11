namespace AadhiCrackers.Application.Common;

/// <summary>
/// The object store refused or could not be reached. Deliberately distinct from
/// <see cref="Domain.Exceptions.DomainException"/> (a 400 — the caller sent something invalid):
/// this is a 502, the request was fine and OUR dependency failed. The message is written to be
/// shown to the shop owner, because the realistic causes — a mistyped R2 access key, a bucket in
/// another account, a token whose permissions are read-only — are all things only they can fix,
/// and a bare "500 Internal Server Error" would leave them guessing.
/// </summary>
public class FileStorageException : Exception
{
    public FileStorageException(string message) : base(message) { }
    public FileStorageException(string message, Exception innerException) : base(message, innerException) { }
}
