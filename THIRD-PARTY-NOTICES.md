# Third-party notices

Cadence Studio v1.0 references or accesses:

- **NAudio 2.3.0** by Mark Heath and contributors, distributed under the MIT License.
- **TagLibSharp 2.3.0**, distributed under the LGPL-2.1 license as identified by its NuGet package metadata.
- **MusicBrainz Web Service** for open music metadata. Cadence Studio identifies itself and rate-limits requests.
- **Cover Art Archive**, a MusicBrainz and Internet Archive project, for optional front-cover images. Artwork remains copyrighted by its respective rights holders and is cached only for local application display.
- **LRCLIB** for optional online lyrics fallback. Lyrics remain the property of their respective rights holders.
- **Wikipedia / Wikimedia APIs** for artist introductions. Displayed summaries include source attribution and are subject to Wikimedia licensing.

NuGet dependencies are restored during `dotnet restore`. Consult the restored package metadata and provider documentation for complete terms.
