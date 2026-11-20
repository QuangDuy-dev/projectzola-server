using Microsoft.Extensions.Logging.Abstractions;
using MySocialApp.Application.DTOs.Call;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;
using Xunit;

namespace MySocialApp.Tests;

public class CallServiceTests
{
    private static async Task<(MySocialApp.Infrastructure.Data.MySocialAppDbContext context, User user1, User user2, Conversation conv)> SetupTestEnvironment(string dbName)
    {
        var context = TestDbContextFactory.Create(dbName);

        var user1 = new User { Id = Guid.NewGuid(), Username = "user1", DisplayName = "User One" };
        var user2 = new User { Id = Guid.NewGuid(), Username = "user2", DisplayName = "User Two" };

        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Direct,
            Members = new List<ConversationMember>
            {
                new() { UserId = user1.Id },
                new() { UserId = user2.Id }
            }
        };

        context.Users.AddRange(user1, user2);
        context.Conversations.Add(conv);
        await context.SaveChangesAsync();

        return (context, user1, user2, conv);
    }

    [Fact]
    public async Task StartCallAsync_Valid1On1_SetsRingingStatus_AndReturnsDto()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(StartCallAsync_Valid1On1_SetsRingingStatus_AndReturnsDto));
        var service = new CallService(context, NullLogger<CallService>.Instance);

        var request = new StartCallRequest
        {
            ConversationId = conv.Id,
            CalleeId = user2.Id,
            Type = CallType.Voice
        };

        var result = await service.StartCallAsync(user1.Id, request);

        Assert.NotNull(result);
        Assert.Equal(CallStatus.Ringing, result.Status);
        Assert.Equal(user1.Id, result.CallerId);
        Assert.Equal(user2.Id, result.CalleeId);
        Assert.Equal(CallType.Voice, result.Type);
    }

    [Fact]
    public async Task StartCallAsync_GroupConversation_ThrowsInvalidOperationException()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(StartCallAsync_GroupConversation_ThrowsInvalidOperationException));
        conv.Type = ConversationType.Group;
        await context.SaveChangesAsync();

        var service = new CallService(context, NullLogger<CallService>.Instance);

        var request = new StartCallRequest
        {
            ConversationId = conv.Id,
            CalleeId = user2.Id,
            Type = CallType.Video
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartCallAsync(user1.Id, request));
    }

    [Fact]
    public async Task StartCallAsync_CalleeAlreadyInActiveCall_ReturnsBusyStatus_AndGeneratesCallLog()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(StartCallAsync_CalleeAlreadyInActiveCall_ReturnsBusyStatus_AndGeneratesCallLog));
        var user3 = new User { Id = Guid.NewGuid(), Username = "user3", DisplayName = "User Three" };
        context.Users.Add(user3);

        // Put user2 in an active call with user3
        var activeSession = new CallSession
        {
            ConversationId = conv.Id,
            CallerId = user3.Id,
            CalleeId = user2.Id,
            Type = CallType.Voice,
            Status = CallStatus.Connected,
            ConnectedAt = DateTime.UtcNow
        };
        context.CallSessions.Add(activeSession);
        await context.SaveChangesAsync();

        var service = new CallService(context, NullLogger<CallService>.Instance);

        var request = new StartCallRequest
        {
            ConversationId = conv.Id,
            CalleeId = user2.Id,
            Type = CallType.Voice
        };

        var result = await service.StartCallAsync(user1.Id, request);

        Assert.Equal(CallStatus.Busy, result.Status);
        var messages = context.Messages.Where(m => m.ConversationId == conv.Id && m.Type == MessageType.CallLog).ToList();
        Assert.NotEmpty(messages);
    }

    [Fact]
    public async Task AcceptCallAsync_CalleeAccepts_SetsConnectedStatus()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(AcceptCallAsync_CalleeAccepts_SetsConnectedStatus));
        var service = new CallService(context, NullLogger<CallService>.Instance);

        var session = new CallSession
        {
            ConversationId = conv.Id,
            CallerId = user1.Id,
            CalleeId = user2.Id,
            Type = CallType.Video,
            Status = CallStatus.Ringing,
            RingingAt = DateTime.UtcNow
        };
        context.CallSessions.Add(session);
        await context.SaveChangesAsync();

        var result = await service.AcceptCallAsync(session.Id, user2.Id);

        Assert.Equal(CallStatus.Connected, result.Status);
        Assert.NotNull(result.ConnectedAt);
    }

    [Fact]
    public async Task AcceptCallAsync_NonCalleeTriesToAccept_ThrowsUnauthorized()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(AcceptCallAsync_NonCalleeTriesToAccept_ThrowsUnauthorized));
        var service = new CallService(context, NullLogger<CallService>.Instance);

        var session = new CallSession
        {
            ConversationId = conv.Id,
            CallerId = user1.Id,
            CalleeId = user2.Id,
            Type = CallType.Voice,
            Status = CallStatus.Ringing
        };
        context.CallSessions.Add(session);
        await context.SaveChangesAsync();

        // Caller user1 tries to accept own call
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AcceptCallAsync(session.Id, user1.Id));
    }

    [Fact]
    public async Task EndCallAsync_ConnectedCall_SetsEndedStatus_CalculatesDuration_AndGeneratesCallLog()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(EndCallAsync_ConnectedCall_SetsEndedStatus_CalculatesDuration_AndGeneratesCallLog));
        var service = new CallService(context, NullLogger<CallService>.Instance);

        var session = new CallSession
        {
            ConversationId = conv.Id,
            CallerId = user1.Id,
            CalleeId = user2.Id,
            Type = CallType.Voice,
            Status = CallStatus.Connected,
            ConnectedAt = DateTime.UtcNow.AddSeconds(-35)
        };
        context.CallSessions.Add(session);
        await context.SaveChangesAsync();

        var (endedDto, msgDto) = await service.EndCallAsync(session.Id, user1.Id);

        Assert.Equal(CallStatus.Ended, endedDto.Status);
        Assert.True(endedDto.DurationSeconds >= 34);
        Assert.NotNull(msgDto);
        Assert.Equal(MessageType.CallLog.ToString(), msgDto.Type);

        var savedSession = await context.CallSessions.FindAsync(session.Id);
        Assert.NotNull(savedSession);
        Assert.Equal(CallStatus.Ended, savedSession.Status);
        Assert.True(savedSession.DurationSeconds >= 34);
    }

    [Fact]
    public async Task RejectCallAsync_CalleeRejects_SetsRejectedStatus_AndGeneratesCallLog()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(RejectCallAsync_CalleeRejects_SetsRejectedStatus_AndGeneratesCallLog));
        var service = new CallService(context, NullLogger<CallService>.Instance);

        var session = new CallSession
        {
            ConversationId = conv.Id,
            CallerId = user1.Id,
            CalleeId = user2.Id,
            Type = CallType.Video,
            Status = CallStatus.Ringing
        };
        context.CallSessions.Add(session);
        await context.SaveChangesAsync();

        var (endedDto, msgDto) = await service.RejectCallAsync(session.Id, user2.Id);

        Assert.Equal(CallStatus.Rejected, endedDto.Status);
        Assert.NotNull(msgDto);
        Assert.Equal(MessageType.CallLog.ToString(), msgDto.Type);
    }

    [Fact]
    public async Task HandleTimeoutAsync_RingingCallTimesOut_SetsMissedStatus_AndGeneratesCallLog()
    {
        var (context, user1, user2, conv) = await SetupTestEnvironment(nameof(HandleTimeoutAsync_RingingCallTimesOut_SetsMissedStatus_AndGeneratesCallLog));
        var service = new CallService(context, NullLogger<CallService>.Instance);

        var session = new CallSession
        {
            ConversationId = conv.Id,
            CallerId = user1.Id,
            CalleeId = user2.Id,
            Type = CallType.Voice,
            Status = CallStatus.Ringing,
            RingingAt = DateTime.UtcNow.AddSeconds(-50)
        };
        context.CallSessions.Add(session);
        await context.SaveChangesAsync();

        var result = await service.HandleTimeoutAsync(session.Id);

        Assert.NotNull(result);
        var (endedDto, msgDto) = result.Value;
        Assert.Equal(CallStatus.Missed, endedDto.Status);
        Assert.NotNull(msgDto);
        Assert.Contains("nhỡ", msgDto.Content);
    }
}
