# GARbro2 - Visual Novels Resource Browser

Requires [.NET Framework v4.8](https://dotnet.microsoft.com).

[Supported formats](https://morkt.github.io/GARbro/supported.html)

[Download latest release](/../../releases/latest)

## Operation
Browse through the file system to find the file you're looking for.  If you think it's a game archive, try entering it by pressing the `Enter` key. If GARbro2 recognizes the format, including all formats supported by 7-zip, its contents will be displayed just like a regular file system.

Then, you can preview images, including rare formats supported by ImageMagick, see what sprite-sheets would look like when animated and how multiple overlay images would look when combined.

You can also check sound effects and listen to background music in playback mode similar to a playlist.

Some archives are encrypted, so you may be asked for credentials or the name of the game.  If the game is not listed among the presented options, then the archive likely cannot be opened by the current version of GARbro2.

You can extract files from archives by pressing `F4`. In the process, all images and audio can be converted to common formats if the original format is recognized and the corresponding option is set.

When displaying the contents of a file system, GARbro2 assigns types to files based on their name extensions, but it's not always correct. If the types are incorrect, they can be changed by selecting the files and assigning the type manually via the context menu `Assign file type`.

You can also create image previews for any archive type by placing images with the same name as the archives in the `.thumbnails` subfolder of the archive location.

## GUI Hotkeys

<table>
<tr><td><kbd>Enter</kbd></td><td>                   Try to open selected file as archive -OR- playback audio file</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>PgDn</kbd></td><td>    Try to open selected file as archive</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>E</kbd></td><td>       Open current folder in Windows Explorer</td></tr>
<tr><td><kbd>Left Arrow</kbd></td><td>              Go back</td></tr>
<tr><td><kbd>Right Arrow</kbd></td><td>             Go forward</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>PgUp</kbd></td><td>    Go to parent directory</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>O</kbd></td><td>       Choose file to open as archive</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>A</kbd></td><td>       Select all files</td></tr>
<tr><td><kbd>Space</kbd></td><td>                   Select next file</td></tr>
<tr><td><kbd>Backspace</kbd></td><td>               Select previous file</td></tr>
<tr><td><kbd>Numpad +</kbd></td><td>                Select files matching specified mask</td></tr>
<tr><td><kbd>F3</kbd></td><td>                      Create archive</td></tr>
<tr><td><kbd>F4</kbd></td><td>                      Extract selected files</td></tr>
<tr><td><kbd>F5</kbd></td><td>                      Refresh view</td></tr>
<tr><td><kbd>F6</kbd></td><td>                      Convert selected files</td></tr>
<tr><td><kbd>Delete</kbd></td><td>                  Delete selected files</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>H</kbd></td><td>       Fit window to a displayed image</td></tr>
<tr><td><kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>M</kbd></td><td>   Hide menu bar</td></tr>
<tr><td><kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd></td><td>   Hide tool bar</td></tr>
<tr><td><kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd></td><td>   Hide status bar</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>S</kbd></td><td>                   Toggle scaling of large images</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd></td><td>  Reset window size and position (alt. press <kbd>Shift</kbd> during start)</td></tr>
<tr><td><kbd>Right Ctrl</kbd>/<kbd>F12</kbd></td><td>           Boss Key</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Q</kbd></td><td>       Exit</td></tr>
</table>

## License

[MIT License](LICENSE)