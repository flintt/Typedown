using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static async Task WS01(List<string> notes)
    {
        using var c = await Session("e2e WS01");
        var image = Fixture("wv02 + 图.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"><script>parent.__wv02ScriptRan=true</script><rect width=\"20\" height=\"20\" fill=\"red\"/></svg>");
        var address = image.Replace('\\', '/');
        System.IO.File.Copy(image, Fixture("ws01-reference.svg", ""), true);
        // Raw HTML's existing sanitizer rejects file: URLs. Use a relative source, as ordinary HTML image blocks do.
        var file = Fixture("ws01.md", "# WS01\n\n![local](<" + address + ">)\n\n![reference][pic]\n\n[pic]: ws01-reference.svg\n\n<div><img src=\"ws01-reference.svg\"></div>\n");
        var id = await Open(c, file);
        var windowId = await WindowIdOf(c, id);
        async Task<JToken?> Eval(string script) => (await c.Call("test.editor.eval", new { windowId, script }))["result"];
        Check(await Eventually(async () => (bool?)await Eval("[...document.querySelectorAll('#ag-editor-id img')].some(i=>i.complete && i.naturalWidth===20)") == true, 15000), "local SVG is drawn");
        await Eval(@"(() => {
            const img = [...document.querySelectorAll('#ag-editor-id img')].find(i=>i.complete && i.naturalWidth===20);
            window.__wv02 = { origin: location.origin, endpoint: img.src, scriptRan: !!window.__wv02ScriptRan };
            try { const c=document.createElement('canvas'); c.width=c.height=20; c.getContext('2d').drawImage(img,0,0); window.__wv02.canvas=c.toDataURL().startsWith('data:image/png'); }
            catch(e) { window.__wv02.canvas=false; }
            fetch(img.src).then(r=>{window.__wv02.fetch=r.status;},()=>{window.__wv02.fetch='blocked';});
            const script=document.createElement('script'); script.src=img.src;
            script.onload=()=>{window.__wv02.script='loaded';}; script.onerror=()=>{window.__wv02.script='blocked';}; document.head.appendChild(script);
            const frame=document.createElement('iframe'); frame.src=img.src; document.body.appendChild(frame);
        })()");
        Check(await Eventually(async () => (await Eval("window.__wv02"))?["fetch"] != null && (await Eval("window.__wv02"))?["script"] != null, 10000), "resource attempts completed");
        var result = (JObject)(await Eval("window.__wv02"))!;
        notes.Add(result.ToString(Newtonsoft.Json.Formatting.None));
        Check((string?)result["origin"] == "https://editor.invalid", "the installed editor has an HTTPS origin");
        Check((bool?)result["canvas"] == true, "local SVG remains readable for PNG clipboard conversion");
        Check((string?)result["fetch"] is "403" or "blocked", "local image is not a general file-read endpoint");
        Check((string?)result["script"] == "blocked", "local SVG cannot load as a script");
        Check((bool?)await Eval("!!window.__wv02ScriptRan") == false, "SVG script did not run");
        await Eval("document.querySelectorAll('body > iframe').forEach(f=>f.remove())");
        await c.Call("window.setView", new { windowId, mode = "source" });
        await c.Call("window.setView", new { windowId, mode = "visual" });
        Check(await Eventually(async () => (bool?)await Eval(@"(() => {
            const images=[...document.querySelectorAll('#ag-editor-id img')];
            return images.length===3 && images.every(i=>{
                if(!i.complete || i.naturalWidth!==20 || i.crossOrigin!=='anonymous') return false;
                try {const c=document.createElement('canvas');c.width=c.height=20;c.getContext('2d').drawImage(i,0,0);return c.toDataURL().startsWith('data:image/png');} catch {return false;}
            });
        })()") == true, 15000), "inline, reference and raw-HTML SVG images remain readable after redraw");

        // A configured PlantUML server with no CORS headers: copying its PNG must still work without a general proxy.
        var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        var serverAddress = "http://127.0.0.1:" + ((System.Net.IPEndPoint)server.LocalEndpoint).Port + "/plantuml";
        var png = System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var serving = Task.Run(async () => {
            try { while (true) {
                using var connection = await server.AcceptTcpClientAsync();
                using var stream = connection.GetStream();
                var request = new byte[8192];
                await stream.ReadAsync(request);
                var header = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: " + png.Length + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(png);
            } } catch (System.Net.Sockets.SocketException) { }
        });
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "PlantUmlServer", value = serverAddress });
            await c.Call("test.settings.set", new { windowId, name = "RenderPlantUml", value = true });
            var requestUrl = Newtonsoft.Json.JsonConvert.SerializeObject(serverAddress + "/png/~h4142");
            await Eval("(() => { const id='ws01-png';window.__ws01Png=null;const l=e=>{const m=JSON.parse(e.data);if(m.name===id){window.__ws01Png=m.args;window.chrome.webview.removeEventListener('message',l);}};window.chrome.webview.addEventListener('message',l);window.chrome.webview.postMessage(JSON.stringify({type:'invoke',name:'GetPlantUmlPng',id,args:" + requestUrl + "}));})()");
            Check(await Eventually(async () => (await Eval("window.__ws01Png"))?.Type == JTokenType.Object, 20000), "configured PNG request completed");
            var response = await Eval("window.__ws01Png");
            Check((int?)response?["code"] == 0 && (string?)response?["data"] == System.Convert.ToBase64String(png), "configured non-CORS server PNG remains available for copying");
            await Open(c, Fixture("ws01-http.md", "# HTTP images\n\n![http](" + serverAddress + "/photo.png)\n\n```plantuml\nAlice -> Bob: hello\n```\n"));
            Check(await Eventually(async () => (bool?)await Eval(@"(() => {
                const images=[...document.querySelectorAll('#ag-editor-id img')];
                return images.length===2 && images.every(i=>i.complete && i.naturalWidth===1 && i.src.startsWith(window.httpImageEndpoint+'?'));
            })()") == true, 15000), "HTTP-only images and PlantUML preview draw through the image-only origin");
        }
        finally
        {
            server.Stop();
            await serving.WaitAsync(System.TimeSpan.FromSeconds(5));
            await c.Call("test.settings.set", new { windowId, name = "RenderPlantUml", value = false });
            await c.Call("test.settings.set", new { windowId, name = "PlantUmlServer", value = "" });
        }
    }
}
