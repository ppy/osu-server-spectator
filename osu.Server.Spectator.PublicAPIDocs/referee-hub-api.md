# Referee hub API

> [!IMPORTANT]
> This API is in **active development**.
> Its contract should be considered **unstable** until this notice is removed.

The referee hub API is intended to support community tournaments which occur via osu!(lazer) realtime multiplayer.
It allows creating and managing gameplay in multiple multiplayer rooms at a time, outside of the game client.
Additionally, it provides real-time updates about the state of the managed multiplayer rooms.

To referee a multiplayer room, an osu! account is required.
Referees identify themselves to the hub via an osu! account and perform actions in the room as that account.
They will also be visible to players in the room as that account.

> [!NOTE]
> - The API does **not** provide direct access to the chat channel associated with the managed multiplayer rooms.
 The extent of this API is limited to providing IDs of the chat channels in question.
 For receiving messages from them, please use the [osu!web chat APIs](https://osu.ppy.sh/docs/index.html#chat).
> - The number of open rooms at any given time for a single referee (as identified by the osu! account used) is limited.
>   A single account can have:
>   - up to 4 open rooms, if they are not a bot account
>   - up to 50 open rooms, if they are a bot account

## Connecting to the hub

The route URL for the referee hub is `/referee`.

To connect to the referee hub, a JSON Web Token issued by osu!web is required.
To be able to receive a token, you must first register an OAuth application on the osu! website.
See [Registering an OAuth application](https://osu.ppy.sh/docs/index.html#registering-an-oauth-application) in osu!web
documentation for more details.

Having registered an OAuth application, you can proceed to request a token from osu!web.
Note that you **must** include the `multiplayer.write_manage` scope when requesting the token.

As per [osu!web documentation](https://osu.ppy.sh/docs/index.html#scopes), there are two possible grant types
for authenticating against osu!web with the `multiplayer.write_manage` scope.

### Client credentials grant

The `client_credentials` grant type is recommended in use cases wherein a referee uses an application wholly contained
on their computer to connect to the referee hub (a "thick client").
In such cases the client secret effectively serves as the referee's password replacement.

When attempting to connect via the `client_credentials` grant, the `delegate` scope must also be requested from osu!web.
This is because the token will allow its wielder to perform actions on behalf of the osu! account that owns the OAuth
client.

To request the token using the client credentials grant, follow
[the instructions in osu!web docs](https://osu.ppy.sh/docs/index.html#client-credentials-grant).

### Authorization code grant

The `authorization_code` grant type is recommended in use cases wherein a referee uses an application whose core logic
is hosted on a third-party server, and the referee only interacts with the frontend of the application
(a "thin client").
In such cases the client secret serves to authenticate *the third party hosting the application*.

When attempting to connect via the `authorization_code` grant, the `delegate` scope is no longer required, because
it is implied - the authorization code flow automatically implies being able to perform actions on behalf of the referee
who's going through the flow.

> [!WARNING]
> Note that there are limitations when using the authorization code grant.
> Unless the osu! account that owns the OAuth client used for authorization is **a bot account**, only **the owner
> of the client themselves** can successfully complete the authorization code flow using their client.

The authorization code flow is described in detail
in [osu!web docs](https://osu.ppy.sh/docs/index.html#authorization-code-grant).

### Completing the connection

Finally, with the acquired token, you can establish a connection with the referee hub.
See example in TypeScript code below:

```ts
const connection = new HubConnectionBuilder()
    .withUrl(new URL('/referee', SPECTATOR_SERVER_URL).toString(), {
        accessTokenFactory: () => accessToken
    })
    .build();
```

> [!NOTE]
> When accessing the hub from a browser context, you may need to pass the token via query string.
> This is because of browser API limitations;
> see [relevant SignalR docs](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0#built-in-jwt-authentication).
> In other contexts you can use the standard `Authorization: Bearer $TOKEN` header.
> 
> The TypeScript snippet above handles this automatically because it uses the official SignalR JavaScript client
> which contains the `accessTokenFactory` helper property which automatically handles this, among other things such as
> token revocation.
> All official SignalR clients should contain a variant of `AccessTokenFactory` and its use is recommended if available.

## Hub methods & events

The hub offers bidirectional communication.

- You can request to invoke **methods** which are basically remote procedure calls handled by the server.
  All available methods are listed in the
  [`IRefereeHubServer`](xref:osu.Server.Spectator.Hubs.Referee.IRefereeHubServer) interface.
  Some methods will return data directly as a response.
- You will also receive **events** which inform of happenings and changes of state in the room.
  Events can either be provoked by a referee's method invocation, or by players doing something in the room, or
  even automatically in case of countdowns.
  All available event callbacks are listed in the
  [`IRefereeHubClient`](xref:osu.Server.Spectator.Hubs.Referee.IRefereeHubClient) interface.

Your first port of call is likely to be the
[`MakeRoom`](xref:osu.Server.Spectator.Hubs.Referee.IRefereeHubServer.MakeRoom(osu.Server.Spectator.Hubs.Referee.Models.Requests.MakeRoomRequest)) method
which will create a room.
The second one will likely be
[`InvitePlayer`](xref:osu.Server.Spectator.Hubs.Referee.IRefereeHubServer.InvitePlayer(System.Int64,System.Int32)) method
which will invite a player to the room.

After that, the world is your oyster - within the limits of the aforementioned two interfaces, that is.

## Error handling

If invoking a hub method doesn't throw, then the invocation should be presumed to be successful.
Due to SignalR limitations, when a hub invocation throws, basically only a magic string error message is returned,
and if the failure is at application level, the error message will be fully opaque to the consumer;
see [relevant docs](https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs?view=aspnetcore-10.0#handle-errors).

To aid in identifying business-logic relevant errors, the hub can return standardised error messages with error codes
in cases of known failures related to user input or other cases wherein the operation may succeed if its parameters are
adjusted or it is performed in a different state of the room.
These errors are listed in [`ThrowHelper`](xref:osu.Server.Spectator.Hubs.Referee.ThrowHelper).

<!-- TODO: handling disconnections -->