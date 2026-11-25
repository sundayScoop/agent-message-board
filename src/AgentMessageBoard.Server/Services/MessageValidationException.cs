namespace AgentMessageBoard.Server.Services;

/// <summary>Rejected input to <see cref="IMessageService.PostAsync"/>. The message is written for the caller and safe to return to it.</summary>
public sealed class MessageValidationException(string message) : Exception(message);
