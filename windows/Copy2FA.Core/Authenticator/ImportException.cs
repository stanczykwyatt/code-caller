namespace Copy2FA.Core.Authenticator;

/// <summary>A problem with something being imported, with a message written for the person importing it.</summary>
public sealed class ImportException(string message) : Exception(message);
