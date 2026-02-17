---
_layout: landing
---

# osu! spectator server

Welcome to the public documentation for the osu! spectator server.

The spectator server started as a supporting piece for spectating support in [osu!(lazer)](https://github.com/ppy/osu)
and has since grown to additionally support other online functionality such as realtime multiplayer or user presence and
status updates.

The spectator server uses [SignalR](https://learn.microsoft.com/en-us/aspnet/signalr/overview/getting-started/introduction-to-signalr)
as the supporting library and protocol used for communication with its clients.
SignalR has officially supported clients for:

- [.NET](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-10.0),
- [Java](https://learn.microsoft.com/en-us/aspnet/core/signalr/java-client?view=aspnetcore-10.0),
- [JavaScript](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0),
- [Swift](https://learn.microsoft.com/en-us/aspnet/core/signalr/swift-client?view=aspnetcore-10.0).

Several other popular programming languages have their own unofficial client implementations of the SignalR protocol.
Please consult your language's preferred package repository for more details.

Currently, only one SignalR [hub](https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs?view=aspnetcore-10.0)
is available for public consumption by clients other than osu!(lazer).
It is the **referee hub**, intended for supporting community tournaments which occur via real-time multiplayer.
For more information, see [Referee hub API](referee-hub-api.md).