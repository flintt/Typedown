# Typedown User Guide

Typedown is a WYSIWYG Markdown editor for Windows: what you see while you write is the formatted page, and what gets saved is a plain `.md` text file that any editor can open.

This guide covers version 1.3.8. Menu and setting names are given as they appear in the English interface; you can change the interface language under **Settings → General**.

- [1. Quick start](#1-quick-start)
- [2. Four modes](#2-four-modes)
- [3. Formatting and shortcuts](#3-formatting-and-shortcuts)
- [4. Pictures](#4-pictures)
- [5. Picture upload](#5-picture-upload)
- [6. Themes](#6-themes)
- [7. Export, print and share](#7-export-print-and-share)
- [8. FAQ](#8-faq)

---

## 1. Quick start

**Open a file**: File → Open (Ctrl+O), or drag a `.md` file into the window. Each document gets its own tab; switch tabs with Ctrl+Tab / Ctrl+Shift+Tab and close one with Ctrl+W.

**Open a folder**: File → Open Folder. The side bar on the left (Ctrl+Shift+L shows/hides it) has two pages:

- **Files**: the documents in the folder. A single click previews a file (the tab is replaced when you open another file); a double click opens it for real. The magnifier at the top of the side bar searches the whole folder (Ctrl+Shift+F)
- **Outline**: the headings of the current document; click one to jump to it. While you write, the heading you are under is highlighted

**Save**: Ctrl+S; Save As is Ctrl+Shift+S. Unsaved changes are backed up regularly, and if the app closes unexpectedly they are restored the next time you open it. On the next start, the tabs you had open last time come back automatically.

**Settings**: Ctrl+, or the gear in the top-right corner of the window.

---

## 2. Four modes

| Mode | How to turn it on | What it is for |
|---|---|---|
| Visual editing | Default | WYSIWYG: you write directly on the formatted page |
| Source Code Mode | View → Source Code Mode, Ctrl+/ | Edit the raw Markdown |
| Reading Mode | View → Reading Mode, Ctrl+Shift+R | Read-only, so nothing changes by accident; you can still select and copy |
| Focus / Typewriter Mode | F8 / F9 | Focus Mode dims the other paragraphs; Typewriter Mode keeps the current line in the middle of the screen |

F11 toggles full screen.

In Reading Mode the right-click menu only has copying and Select All:

- **Copy**: keeps the formatting when you paste into Word or an email
- **Copy as Plain Text**: drops the Markdown markup and copies the text as it looks on the page

While editing, the right-click menu also has **Copy as Markdown** (Ctrl+Shift+C), which copies the raw Markdown.

**The status bar** (at the bottom of the window; View → Status Bar turns it on and off):

- **Mode button**: its icon shows the current mode (visual, source code, reading); a click lists the modes. The eye next to it goes into Reading Mode in one click, and the "Reading Mode" label takes you back to the mode you came from (Source Code Mode if that is where you were)
- **Current heading**: the heading the caret is under; a click opens the outline
- **Encoding and line endings**: for example "UTF-8 · LF". A click lets you change the line endings (LF / CRLF), convert the file to UTF-8, or reopen a file without a BOM in another encoding such as GB18030 or Big5 (not while there are unsaved changes). Hidden by default: right-click the status bar to show it
- **Save status**: Saved / Not saved / Auto-save paused. Hover over it to see why auto-save paused (for example, a file that could not be read without loss); a click saves
- **Word count**: counted on the text you see on the page (no Markdown markup or link addresses; each Han character or kana counts as a word); with a selection it shows the selected part. A click shows the details: characters with and without spaces, CJK characters, paragraphs, headings, images, links and an estimated reading time, and lets you choose whether the status bar shows characters or words

Right-click the status bar to choose which items it shows; in a window too narrow for all of them, the ones that do not fit go into the "..." menu at the right.

---

## 3. Formatting and shortcuts

In visual editing, typing Markdown syntax turns into formatting right away: `## ` at the start of a line makes a level-2 heading, `- ` starts a list, `> ` a quote, three backticks a code block and `$$` a math block.

| Action | Shortcut |
|---|---|
| Heading 1–6 / Paragraph | Ctrl+1 … Ctrl+6 / Ctrl+0 |
| Increase / decrease heading level | Ctrl+= / Ctrl+- |
| Bold / italic / underline | Ctrl+B / Ctrl+I / Ctrl+U |
| Strikethrough | Alt+Shift+5 |
| Inline code | Ctrl+Shift+` |
| Link / image | Ctrl+K / Ctrl+Shift+I |
| Clear format | Ctrl+\ |
| Numbered list / bullets / task list | Ctrl+Shift+[ / Ctrl+Shift+] / Ctrl+Shift+X |
| Quote / code block / math block / table | Ctrl+Shift+Q / Ctrl+Shift+K / Ctrl+Shift+M / Ctrl+Shift+T |
| Insert paragraph before / after | Ctrl+Shift+Enter / Ctrl+Enter |
| Find / replace / find next | Ctrl+F / Ctrl+H / F3 |
| Paste as plain text | Ctrl+Shift+V |
| Print | Alt+Shift+P |

You can change every shortcut under **Settings → General → Shortcuts**.

The **Paragraph** menu also lets you insert charts (Flow Chart, Sequence Diagram, Mermaid, PlantUML Diagram, Vega Chart), footnotes, a horizontal line, YAML front matter and more.

---

## 4. Pictures

### 4.1 Ways to insert a picture

| Method | How |
|---|---|
| Paste a screenshot | A picture on the clipboard (from a screenshot tool, a browser's "Copy image" and so on): press Ctrl+V |
| Paste a picture file | Copy the picture file in File Explorer, then press Ctrl+V |
| Drag it in | Drag picture files into the window; drag several at once and each gets its own paragraph in order, and one undo takes them all back out |
| Menu | Format → Image (Ctrl+Shift+I); in the "Edit Image" dialog, type a path or click "Browse..." (you can pick several files) |
| By hand | Type `![说明文字](路径)` |

Supported picture formats: `.png .jpg .jpeg .gif .svg .webp .jfif`.

### 4.2 What happens to a picture when you insert it

Open **Settings → Image → Insert image action**. Pictures fall into three kinds, and you pick a separate action for each:

- **Clipboard Image**: screenshots and the like; not copied picture files and not web pictures
- **Local Image**: picture files on your computer (pasted, dragged in or chosen in a dialog)
- **Web Image**: picture addresses starting with `http(s)://`

| Action | Clipboard Image | Local Image | Web Image |
|---|---|---|---|
| **No action** (default) | Saved as PNG in the "Default image root directory"; the document gets an absolute path (`file:///...`) | The document points at the original file | The document points at the web address |
| **Copy to path** | Saved as PNG in the folder you choose | A copy goes into the folder you choose | Downloaded into the folder you choose |
| **Upload image** | Uploaded to your image host; the document gets the web address | Uploaded; the document gets the web address | Downloaded, then uploaded; the document gets the web address |

When you choose "Copy to path", a folder box appears below it; the default is `./images`. A relative path is relative to **the folder the document is in**. You can use these variables:

| Variable | Meaning | Example (document `D:\笔记\周报.md`, date 2026-10-07) |
|---|---|---|
| `${filename}` | Document file name without the extension | `周报` |
| `${filedir}` | Folder the document is in | `D:\笔记` |
| `${year}` `${month}` `${day}` | Today's year, month and day | `2026` `10` `07` |

**Examples:**

| Folder setting | Pictures go to | Written in the document as |
|---|---|---|
| `./images` | `D:\笔记\images\` | `![](./images/截图.png)` |
| `./${filename}.assets` | `D:\笔记\周报.assets\` | `![](./周报.assets/截图.png)` |
| `./images/${year}-${month}` | `D:\笔记\images\2026-10\` | `![](./images/2026-10/截图.png)` |
| `D:\图库\${year}` | `D:\图库\2026\` | Absolute path |

`./${filename}.assets` follows Typora's convention: one picture folder per document. When you move the document, move the folder with it.

**Other related settings** (same page):

- **Default image root directory**: used as the base for relative paths while the document has not been saved yet; clipboard pictures also go here when their action is "No action". The default is `Pictures\Typedown`.
- **Prioritize using relative path**: when the action for local pictures is "No action", absolute paths are rewritten relative to the document. Useful when pictures and documents live in the same folder that you move around as a whole or keep in Git.
- **Add "./" before a relative path**: puts `./` in front of the relative paths from the previous setting.

> Spaces in a path are written as `%20`; non-ASCII characters such as Chinese are kept as they are, for example `![](./images/我的%20截图.png)`.

### 4.3 Recommended setups

**Notes kept locally or in a synced folder (OneDrive, Nutstore and so on)**: pictures travel with the document.

- Clipboard Image: Copy to path, `./${filename}.assets`
- Local Image: Copy to path, `./${filename}.assets`
- Web Image: No action

**Blogging or publishing online**: put every picture on an image host (see section 5).

- Choose "Upload image" for all three kinds
- Or write as in the setup above, and before publishing use **File → Upload local images** to upload everything in one go

### 4.4 The picture's right-click menu

Right-click a picture → **Image**:

| Menu item | What it does |
|---|---|
| Open Image Location | Opens the picture's folder in File Explorer (local pictures only) |
| Copy Image to... | Copies it to a place you choose, **and the document now points at the new location** |
| Move Image to... | Same as above, and deletes the original file (local pictures only) |
| Upload Image | Pick an upload configuration; uploads this picture and replaces its address with the web address (local pictures only) |
| Save Image As... | Saves a copy; the document stays as it is |
| Delete Image File | Deletes the picture file and removes the picture from the document (local pictures only) |

Clicking a picture shows a floating toolbar: Edit, Inline / Align Left / Center / Align Right, Zoom (25%–200%) and Delete. Alignment and zoom write the picture as an `<img>` tag.

---

## 5. Picture upload

### 5.1 Set up in three steps

1. **Settings → Image → Image upload configurations → Add**: give it a name and choose an upload method
2. Fill in the parameters on the configuration page, then click **Test upload → Picker File** and pick a picture to try it. On success the uploaded address is shown. (When the "Enable" switch at the top of the configuration page is off, the configuration does not appear in the upload menus)
3. Back on **Settings → Image**, choose this configuration under **Upload with**

From then on:

- Pictures whose insert action is "Upload image" are uploaded when you insert them
- **File → Upload local images** uploads every local picture in the current document at once
- Right-click a picture → Image → Upload Image uploads just that one

Two upload methods are available today: **S3 / R2 / OSS / COS** (S3-compatible object storage) and **PowerShell** (your own script, which can talk to any image host).

### 5.2 S3 / R2 / OSS / COS

For object storage with an S3 API: Cloudflare R2, Amazon S3, Alibaba Cloud OSS, Tencent Cloud COS, MinIO and so on.

| Field | Description |
|---|---|
| Endpoint | Service address; `https://` is added if you leave it out |
| Region | Region; empty means `us-east-1`; for R2 enter `auto` |
| Bucket | Bucket name |
| Access Key ID / Secret Access Key | Access keys. The secret is stored encrypted with your Windows account, never in plain text |
| Path-style addresses | Turn on for MinIO and some providers |
| Folder in bucket | For example `images/${year}/${month}`; same variables as in 4.2; empty puts files at the bucket root |
| Public URL | The domain people use to see the pictures, for example `https://img.example.com`; empty uses the Endpoint address |

Uploaded files are named `原文件名-8位哈希.扩展名` (original name, an 8-character hash, the extension), for example `截图-3fa2c91b.png`; a pasted screenshot is named `image-哈希.png` (`image-` plus the hash). The same picture always gets the same name, so it is never uploaded as several copies.

**Example: Cloudflare R2**

| Field | Value |
|---|---|
| Endpoint | `https://<账户ID>.r2.cloudflarestorage.com` |
| Region | `auto` |
| Bucket | `blog-images` |
| Access Key ID / Secret | Create them in R2 → Manage R2 API Tokens, with "Object Read & Write" permission |
| Folder in bucket | `images/${year}/${month}` |
| Public URL | The custom domain bound to the bucket, or `https://pub-xxxx.r2.dev` after enabling public access |

After you insert `截图.png`, the document contains:
`![截图](https://img.example.com/images/2026/10/截图-3fa2c91b.png)`

**Example: Tencent Cloud COS**

| Field | Value |
|---|---|
| Endpoint | `https://cos.ap-guangzhou.myqcloud.com` (use the bucket's region) |
| Region | `ap-guangzhou` |
| Bucket | The full bucket name, for example `blog-1250000000` |
| Public URL | The bound CDN domain, or `https://blog-1250000000.cos.ap-guangzhou.myqcloud.com` |

**Example: MinIO (self-hosted)**

| Field | Value |
|---|---|
| Endpoint | `http://192.168.1.10:9000` |
| Region | Leave empty |
| Path-style addresses | On |
| Public URL | Leave empty, or your reverse proxy's domain |

> The bucket must allow public read access, or other people will not see the pictures in your document. For the exact Endpoint and Region format, follow your provider's S3-compatibility documentation.

### 5.3 PowerShell script

The script defines an `Upload-Image` function. Typedown calls it for each picture it uploads, passing the picture's full path; **the last line the function outputs** is the picture's address.

```powershell
# Upload file and return URL
function Upload-Image($FilePath)
{
    return $FilePath
}
```

Things to know:

- It runs in Windows PowerShell 5.1 (`powershell.exe`), which comes with Windows, not PowerShell 7
- Each picture gets at most 60 seconds; a timeout counts as a failure
- Only the last non-empty line of output is taken as the address; anything printed before it is ignored
- To report an error, let the script throw (`throw "..."`); the message appears in the error dialog
- Use **Import From File / Save to File** to manage the script

**Example 1: PicGo command line**

First install [Node.js](https://nodejs.org/), then run `npm install picgo -g` to install the PicGo command-line version (PicGo-Core), and configure your image host with `picgo set uploader`. The PicGo **desktop app** does not include the `picgo` command, so this example does not work with it as is.

```powershell
function Upload-Image($FilePath)
{
    $out = picgo upload "$FilePath" 2>&1 | Out-String
    $url = ($out -split "`r?`n" | Where-Object { $_ -match '^https?://' } | Select-Object -Last 1)
    if (-not $url) { throw "PicGo 没有返回网址：$out" }
    $url.Trim()
}
```

**Example 2: the HTTP API of a self-hosted image host** (here an API that returns JSON `{"url": "..."}`)

`curl.exe` comes with Windows 10 (1803 and later) and Windows 11; nothing to install. Note that you must write `curl.exe`: in PowerShell, plain `curl` is a different command. Replace `YOUR_TOKEN` with the token your image host gave you.

```powershell
function Upload-Image($FilePath)
{
    $json = curl.exe -sS -F "file=@$FilePath" -H "Authorization: Bearer YOUR_TOKEN" https://img.example.com/api/upload
    if ($LASTEXITCODE -ne 0) { throw "curl 上传失败（退出码 $LASTEXITCODE）" }
    $result = $json | ConvertFrom-Json
    if (-not $result.url) { throw "接口没有返回网址：$json" }
    $result.url
}
```

**Example 3: copy to a website folder or a synced folder**

Uses only built-in PowerShell commands; nothing to install.

```powershell
function Upload-Image($FilePath)
{
    $month = Get-Date -Format yyyy-MM
    $dir = "D:\site\static\img\$month"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $name = [IO.Path]::GetFileName($FilePath)
    Copy-Item -LiteralPath $FilePath -Destination $dir -Force
    "https://example.com/img/$month/" + [Uri]::EscapeDataString($name)
}
```

**Example 4: rclone (works with dozens of cloud drives and storage services)**

First download rclone from [rclone.org](https://rclone.org/downloads/), put it on your PATH, and use `rclone config` to set up a remote named `myremote`.

```powershell
function Upload-Image($FilePath)
{
    $name = [IO.Path]::GetFileName($FilePath)
    rclone copyto "$FilePath" "myremote:images/$name" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "rclone 上传失败" }
    "https://img.example.com/images/$name"
}
```

### 5.4 File → Upload local images

Uploads every **local picture** in the current document and replaces its address in the document with the web address:

- Covers `![](...)`, reference-style images and `<img src="...">`; web addresses, `data:` images and anything inside code blocks are left alone
- A file that appears several times in the document is uploaded only once
- The replacement happens in one step, so **a single undo (Ctrl+Z) puts all the local paths back**
- Pictures that fail to upload stay as they were, and the reasons are listed at the end

**Upload history**: a picture already uploaded with the same configuration is not uploaded again; its earlier address is used. If you delete files on your image host, go to **Settings → Image → Upload history** and click "Clear" so they are uploaded again next time.

When you **Share to HedgeDoc** a document that has local pictures, Typedown first asks whether to upload them: readers of the note cannot see files on your computer.

---

## 6. Themes

Switch themes under **View → Theme**: Light, Dark, Black (OLED), plus several built-in color schemes (Sepia, Solarized Light, Solarized Dark, Nord, Dracula, Gruvbox Dark).

**Custom themes**:

- **View → Theme → Custom theme…** opens the theme designer; adjust the colors and export the CSS
- Put the CSS file in the themes folder (for the installer and portable versions this is `文档\Typedown\themes\`, i.e. `Documents\Typedown\themes\`; the Store version keeps it in its app data under `LocalState\themes\`; **Settings → Appearance → Open the themes folder** opens it directly), then click **View → Theme → Reload themes** and the new theme appears in the menu
- The accent color set in a theme is also used for the selection marks in the outline and the file tree, and for the switches, buttons and check boxes in the settings and dialogs; a theme without one uses the Windows accent color

---

## 7. Export, print and share

- **File → Export**: HTML, PDF (with bookmarks), plain text. Export formats and options are set under **Settings → Export**
- **File → Print**: Alt+Shift+P
- **File → Share to HedgeDoc…**: publishes the document to a HedgeDoc server and gives you a link

---

## 8. FAQ

**Pictures do not show**

- Local pictures: check that the path is correct relative to the document's folder; while the document has not been saved, relative paths are based on the "Default image root directory"
- Web pictures: check that the address opens in a browser

**Picture upload fails**

- First try **Test upload** on the configuration page and read the error message
- Make sure the configuration's "Enable" switch is on and that it is selected under **Settings → Image → Upload with**
- S3: check the Endpoint, Region, key permissions and the bucket's public read setting
- PowerShell: make sure the script's last output is the address; you can run `Upload-Image 'C:\某张图片.png'` by hand in a PowerShell window first and look at the output

**Reporting a problem**

Go to **Settings → About** and click **Send Feedback**. Include the version number (shown on the same page) and the steps to reproduce; click **Open Log Folder** and attach the `debug.log` from it. The log stays on your computer and is never uploaded automatically.
