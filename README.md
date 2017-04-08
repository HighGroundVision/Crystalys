# DOTA2 Game Client

[![highgroundvision MyGet Build Status](https://www.myget.org/BuildSource/Badge/highgroundvision?identifier=bcc46394-8ef3-41bd-b5b9-655e13bfbf21)](https://www.myget.org/feed/highgroundvision/package/nuget/HGV.Crystalys)

Uses [SteamKit2](https://github.com/SteamRE/SteamKit) to interoperate with Valve's Steam network. Its primay function is to download DOTA2 match replays. Other features maybe added in the future.

**Project Status:**

- Can download match replays. Yippy!
- Async support. Yippy!

## Getting Started

To download match replays as byte arrays supply a valid username and password and match id to the clients constructor then call the DownloadReplay method.

You can now download this project as a NuGet package [Nuget Gallery](https://www.nuget.org/packages/HGV.Crystalys/) 

### Application Settings

A settings.app.config file should be created in the bin directory the following values. 
Note: These test are not setup for Steam Guard please disable it on the account before running tests.
```
<appSettings>
	<add key="Steam:Username" value="" />
	<add key="Steam:Password" value="" />
	<add key="Dota:MatchId" value="" />
</appSettings>
```

## License

See the LICENSE file.

## Help

If you have any questions, you can tweet us at [@DotaHGV](https://twitter.com/DotaHGV)

## Authors and Acknowledgements

Crystalys is maintained and development by [HGV](http://www.highgroundvision.com), a leading Dota 2 data visualization and analysis web site. HGV's Team:

* [Jamie Webster](https://github.com/RGBKnights) 
* [Graham Clifford](https://github.com/gclifford)

Special thanks to the following people:

* [Ryan Stecker](https://github.com/VoiDeD) built steam kit [SteamKit2](https://github.com/SteamRE/SteamKit).
