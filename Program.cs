using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using dotenv.net;

DotEnv.Load();

var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
    ?? throw new InvalidOperationException("DISCORD_TOKEN not set");
var hubId = ulong.Parse(Environment.GetEnvironmentVariable("HUB_CHANNEL_ID")
    ?? throw new InvalidOperationException("HUB_CHANNEL_ID not set"));


var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates
});

var tempChannels = new ConcurrentDictionary<ulong, byte>();

client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };

client.UserVoiceStateUpdated += (user, before, after) =>
{
    _ = Task.Run(() => HandleVoiceUpdate(user, before, after));
    return Task.CompletedTask;
};

client.Ready += () =>
{
    _ = Task.Run(SweepOrphans);
    return Task.CompletedTask;
};

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await Task.Delay(Timeout.Infinite);

async Task SweepOrphans()
{
    var hub = client.GetChannel(hubId) as SocketVoiceChannel;
    if (hub?.CategoryId is not ulong categoryId) return;
    
    var category = hub.Guild.GetCategoryChannel(categoryId);

    foreach (var voice in category.Channels.OfType<SocketVoiceChannel>())
    {
        if (voice.Id == hubId) continue;

        if (voice.ConnectedUsers.Count > 0)
        {
            tempChannels.TryAdd(voice.Id, 0);
            continue;
        }
        
        await voice.DeleteAsync();
    }
}

async Task HandleVoiceUpdate(SocketUser user, SocketVoiceState before, SocketVoiceState after)
{
    try
    {
        if (before.VoiceChannel is { } oldRoom
            && tempChannels.ContainsKey(oldRoom.Id)
            && oldRoom.ConnectedUsers.Count == 0)
        {
            tempChannels.TryRemove(oldRoom.Id, out _);
            await oldRoom.DeleteAsync();
        }

        if (after.VoiceChannel?.Id == hubId && user is SocketGuildUser member)
        {
            var hub = after.VoiceChannel;
            var room = await hub.Guild.CreateVoiceChannelAsync(
                $"{member.DisplayName}'s Room",
                props => props.CategoryId = hub.CategoryId);

            tempChannels.TryAdd(room.Id, 0);

            await room.AddPermissionOverwriteAsync(member, new OverwritePermissions(
                manageChannel: PermValue.Allow,
                moveMembers: PermValue.Allow));

            await member.ModifyAsync(p => p.Channel = room);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
}