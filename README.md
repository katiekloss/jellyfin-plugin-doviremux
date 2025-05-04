# jellyfin-plugin-doviremux
A giant hack that remuxes my Dolby Vision videos from MKV to MP4, so that my WebOS-based LG TV can play them. Install at your own risk.

## How to use
> [!CAUTION]
>  Make sure you have enough storage space first! Remuxed MP4s will be the same size as, and be placed in the same directory as, the original media.

- Download the latest release and unzip it in your plugins directory, e.g. `/var/db/jellyfin/plugins`
- If you want to limit the plugin to a specific library/show/season/movie/whatever:
  - Create `Jellyfin.Plugin.DoViRemux.xml` in the configuration directory (e.g. `/var/db/jellyfin/plugins/configurations`)
  - Paste in:
    > ```xml
    >  <?xml version="1.0" encoding="utf-8"?>
    >  <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
    >    <IncludeAncestorIds>a656b907eb3a73532e40e44b968d0225</IncludeAncestorIds>
    >  </PluginConfiguration>
    >  ```
  - Replace `IncludeAncestorIds` with the ID of the parent item you want to remux. Usually you can open its page in Jellyfin and take the value of the `id` parameter in the address bar. If you later want to remux **everything**, leave that property completely empty and restart Jellyfin.
- Restart Jellyfin
- Run the "Remux Dolby Vision MKVs" scheduled task
- Run a library scan to detect (and merge) the resulting MP4s

## Roadmap
- [x] Generate remuxed MP4s
- [x] Merge remuxes into the original item (as a new "Version" in the Jellyfin UI)
- [ ] Add a configuration page because XML sucks
- [ ] Add some additional options for constraining what to remux
- [ ] Use dovi_tool to convert profile 7.6 to 8.1, if your TV *really* sucks like mine

## Contributing
I wanted this to have a very seamless F5-able dev experience when developing locally; not "build, then drag-and-drop the DLL into the plugins directory, then launch Jellyfin".

I hacked this together with `launch.json` and `tasks.json` in the `.vscode` directory. If you want to run this locally the same way that I do, you'll need to change the paths there to reflect your own machine.

Specifically:
- `launch.json`: working directory for your `jellyfin` repo clone, your Jellyfin.Server build output, and `jellyfin-web` `dist/` directory
- `tasks.json`: path to the plugin folder within your Jellyfin's `plugins/` directory (doesn't need to exist previously)

Make sure you've built both `Jellyfin.Server` and `jellyfin-web` before launching!
