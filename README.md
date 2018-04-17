# DOTA2 Game Client

[![Build status](https://ci.appveyor.com/api/projects/status/w4j21tvmftda3npk?svg=true)](https://ci.appveyor.com/project/RGBKnights/basilius)

Uses [SteamKit2](https://github.com/SteamRE/SteamKit) to interoperate with Valve's Steam network. Its primary function is to download DOTA2 match replays. Other features maybe added in the future.

**Project Status:**

- Can download match replays. Yippy!
- Async support. Yippy!
- End of Match meta-data. Yippy!

## Getting Started

To download match replays as byte arrays supply a valid user-name and password and match id to the clients constructor then call the DownloadReplay method.

You can now download this project as a NuGet package through [Nuget Gallery](https://www.nuget.org/packages/HGV.Crystalys/)

## License

See the LICENSE file.

## Help

If you have any questions, you can tweet us at [@DotaHGV](https://twitter.com/DotaHGV)

## Authors and Acknowledgments

Crystalys is maintained and development by [HGV](http://www.highgroundvision.com), a leading Dota 2 data visualization and analysis web site. HGV's Team:

* [Jamie Webster](https://github.com/RGBKnights) 
* [Graham Clifford](https://github.com/gclifford)

Special thanks to the following people:

* [Ryan Stecker](https://github.com/VoiDeD) built steam kit [SteamKit2](https://github.com/SteamRE/SteamKit).
