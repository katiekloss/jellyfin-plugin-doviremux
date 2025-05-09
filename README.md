# jellyfin-plugin-doviremux
A giant hack that remuxes my Dolby Vision videos from MKV to MP4, so that my WebOS-based LG TV can play them.

## How to use
> [!WARNING]
>  Make sure you have enough storage space first! Remuxed MP4s will be the same size as, and be placed in the same directory as, the original media.

- Add a new plugin repository with the URL: https://raw.githubusercontent.com/katiekloss/jellyfin-plugins/main/manifest.json
- Install the "DoVi Remux" plugin
- Restart Jellyfin
- Run the "Remux Dolby Vision MKVs" scheduled task
- If you don't want the remuxed versions to appear as their own items in the library, use the separate [Merge Versions plugin](https://github.com/danieladov/jellyfin-plugin-mergeversions)
  - This used to be core functionality, but the other plugin does it better

### Config options
- Include Ancestor IDs
  - Used if you want to limit the plugin to a specific library/show/season/movie/whatever:
    - Update the "Include Ancestor IDs" config option with the ID of the parent item(s) you want to remux. Usually you can open its page in Jellyfin and take the value of the `id` parameter in the address bar.
    - If you later want to remux **everything**, leave that option completely empty.
- Skip/cleanup items watched by
  - When set to a username, the plugin won't remux otherwise-eligible items that this user has already watched. It will also delete remuxes once the user has watched them.
  - The cleanup functionality can be triggered by running the "Clean up Dolby Vision remuxes" scheduled task, which is unscheduled by default (I have mine set to run every 6 hours).

## Roadmap
- [x] Generate remuxed MP4s
- [x] ~~Merge remuxes into the original item (as a new "Version" in the Jellyfin UI)~~ *out of scope*
- [x] Add a configuration page because XML sucks
- [x] Add some additional options for constraining what to remux
- [x] Delete remuxes once they've been watched
- [ ] Remux profile 5 content
- [ ] Use [dovi_tool](https://github.com/quietvoid/dovi_tool) to convert profile 7.6 to 8.1, if your TV *really* sucks like mine

## Contributing
I wanted this to have a very seamless F5-able dev experience when developing locally; not "build, then drag-and-drop the DLL into the plugins directory, then launch Jellyfin".

I hacked this together with `launch.json` and `tasks.json` in the `.vscode` directory. If you want to run this locally the same way that I do, you'll need to change the paths there to reflect your own machine.

The paths in question are anything that has "katie" along the way. Specifically:
- `launch.json`: working directory for your `jellyfin` repo clone, your Jellyfin.Server build output, and `jellyfin-web` `dist/` directory
- `tasks.json`: path to the plugin folder within your Jellyfin's `plugins/` directory (doesn't need to exist previously)

Make sure you've built both `Jellyfin.Server` and `jellyfin-web` before launching!
