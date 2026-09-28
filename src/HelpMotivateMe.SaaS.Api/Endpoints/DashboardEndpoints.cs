using System.Net;
using HelpMotivateMe.SaaS.Api.Options;
using HelpMotivateMe.SaaS.Api.Persistence;
using HelpMotivateMe.SaaS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpMotivateMe.SaaS.Api.Endpoints;

public static class DashboardEndpoints
{
    public static Task HandleRootAsync(HttpContext context, ControlPlaneDbContext db,
        TenantStartQueue startQueue, CancellationToken cancellationToken) =>
        IsControlPlaneHost(context.Request.Host.Host)
            ? WriteSignupAsync(context, cancellationToken)
            : WriteWakePageAsync(context, db, startQueue, cancellationToken);

    public static Task WriteAdminAsync(HttpContext context, IOptions<TenantRuntimeOptions> options,
      CancellationToken cancellationToken) =>
      WritePageAsync(context, "Workspace administration",
        AdminBody
          .Replace("IDLE_TIMEOUT", options.Value.IdleTimeoutMinutes.ToString())
          .Replace("IDLE_SWEEP", options.Value.IdleSweepSeconds.ToString()),
        cancellationToken);

    public static Task WriteWorkspaceAsync(Guid id, HttpContext context, CancellationToken cancellationToken) =>
        WritePageAsync(context, "Workspace settings", WorkspaceBody.Replace("TENANT_ID", id.ToString()),
            cancellationToken);

    public static Task HandleFallbackAsync(HttpContext context, ControlPlaneDbContext db,
        TenantStartQueue startQueue, CancellationToken cancellationToken)
    {
        if (!IsControlPlaneHost(context.Request.Host.Host))
            return WriteWakePageAsync(context, db, startQueue, cancellationToken);

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return context.Response.WriteAsJsonAsync(new { message = "Not found." }, cancellationToken);
    }

    private static bool IsControlPlaneHost(string host) =>
        string.Equals(host, "saas.localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteWakePageAsync(HttpContext context, ControlPlaneDbContext db,
        TenantStartQueue startQueue, CancellationToken cancellationToken)
    {
        var host = context.Request.Host.Host.ToLowerInvariant();
        var subdomain = host.EndsWith(".localhost", StringComparison.Ordinal)
            ? host[..^".localhost".Length]
            : string.Empty;
        var tenant = await db.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Subdomain == subdomain, cancellationToken);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await WritePageAsync(context, "Workspace not found",
                $"<section class=\"wake\"><h1>Workspace not found</h1><p><strong>{WebUtility.HtmlEncode(host)}</strong> is not a provisioned workspace.</p><a class=\"text-link\" href=\"http://saas.localhost:8088\">Create a workspace</a></section>",
                cancellationToken);
            return;
        }

        startQueue.Enqueue(tenant.Id);
        await WritePageAsync(context, "Starting workspace",
            $"<meta http-equiv=\"refresh\" content=\"2\"><section class=\"wake\"><div class=\"pulse\"></div><p class=\"eyebrow\">Waking your workspace</p><h1>Starting {WebUtility.HtmlEncode(tenant.Subdomain)}</h1><p>Your data is safe. This page will continue automatically when the workspace is healthy.</p></section>",
            cancellationToken);
    }

    private static Task WriteSignupAsync(HttpContext context, CancellationToken cancellationToken) =>
        WritePageAsync(context, "Create your workspace", SignupBody, cancellationToken);

    private static Task WritePageAsync(HttpContext context, string title, string body,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(Page(title, body), cancellationToken);
    }

    private static string Page(string title, string body)
    {
      var adminLink = title == "Workspace administration" ? "<a href=\"/admin\">Admin</a>" : string.Empty;
      return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{WebUtility.HtmlEncode(title)}} · Help Motivate Me</title>
          <style>
            :root { color-scheme:light; --ink:#17211d; --muted:#5f6963; --paper:#f6f3ea; --surface:#fffdf7; --line:#cbd0c8; --green:#176b4d; --green-dark:#0f4935; --coral:#d34b2c; --blue:#28618a; }
            * { box-sizing:border-box; }
            body { margin:0; min-height:100vh; color:var(--ink); background:radial-gradient(circle at 85% 8%,#dceadf 0 11%,transparent 32%),linear-gradient(145deg,#fbfaf5,var(--paper)); font-family:'Trebuchet MS','Gill Sans',sans-serif; letter-spacing:0; }
            header { border-bottom:1px solid var(--line); background:rgba(255,253,247,.84); backdrop-filter:blur(12px); }
            nav { width:min(1080px,calc(100% - 32px)); min-height:64px; margin:auto; display:flex; align-items:center; justify-content:space-between; gap:20px; }
            .brand { color:var(--ink); font:700 1.05rem Georgia,serif; text-decoration:none; }
            nav div { display:flex; gap:20px; }
            nav div a { color:var(--muted); font-size:.88rem; font-weight:700; text-decoration:none; }
            main { width:min(1080px,calc(100% - 32px)); margin:auto; padding:56px 0 80px; }
            h1,h2,h3 { font-family:Georgia,'Times New Roman',serif; font-weight:500; letter-spacing:0; }
            h1 { max-width:760px; margin:8px 0 16px; font-size:clamp(2.4rem,7vw,5rem); line-height:1; }
            h2 { margin:0 0 8px; font-size:1.65rem; }
            h3 { margin:0 0 8px; font-size:1.2rem; }
            p { color:var(--muted); line-height:1.55; }
            .lede { max-width:690px; font-size:1.08rem; }
            .eyebrow { margin:0; color:var(--coral); font-size:.74rem; font-weight:800; text-transform:uppercase; }
            .grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:16px; }
            .plans { grid-template-columns:repeat(3,minmax(0,1fr)); margin:40px 0 32px; }
            .plan { position:relative; display:block; min-height:230px; padding:24px; border:1px solid var(--line); border-radius:8px; background:var(--surface); cursor:pointer; }
            .plan:has(input:checked) { border:2px solid var(--green); padding:23px; box-shadow:0 10px 30px rgba(23,107,77,.1); }
            .plan input { position:absolute; top:22px; right:22px; width:19px; height:19px; accent-color:var(--green); }
            .plan strong { display:block; font:500 1.55rem Georgia,serif; }
            .price { margin:8px 0 18px; color:var(--ink); font-weight:800; }
            .plan ul { margin:0; padding-left:20px; color:var(--muted); line-height:1.8; }
            .form-section { margin-top:28px; padding-top:28px; border-top:1px solid var(--line); }
            .fields { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:18px; margin-top:20px; }
            label.field { display:grid; gap:7px; color:var(--ink); font-size:.78rem; font-weight:800; text-transform:uppercase; }
            input,select { width:100%; min-height:46px; border:1px solid #aeb6ad; border-radius:4px; background:white; padding:10px 12px; color:var(--ink); font:1rem 'Trebuchet MS',sans-serif; }
            input:focus,select:focus,button:focus-visible,a:focus-visible { outline:3px solid rgba(40,97,138,.3); outline-offset:2px; }
            .check { display:flex; align-items:flex-start; gap:10px; font-weight:700; }
            .check input { width:19px; min-height:19px; margin-top:2px; accent-color:var(--green); }
            .ai-panel { margin-top:18px; padding:20px; border-left:4px solid var(--blue); background:#edf4f6; }
            .ai-panel .fields { margin-top:16px; }
            button,.button { min-height:44px; border:0; border-radius:4px; padding:0 18px; display:inline-flex; align-items:center; justify-content:center; color:white; background:var(--green); font:800 .9rem 'Trebuchet MS',sans-serif; text-decoration:none; cursor:pointer; }
            button:hover,.button:hover { background:var(--green-dark); }
            button.secondary { background:var(--ink); }
            button.danger { background:var(--coral); }
            button:disabled { cursor:wait; opacity:.6; }
            .submit-row,.actions { display:flex; align-items:center; flex-wrap:wrap; gap:10px; margin-top:24px; }
            .message { min-height:24px; color:var(--coral); font-weight:700; }
            .self-host { margin-top:44px; padding:28px 0; border-block:1px solid var(--line); display:flex; justify-content:space-between; align-items:center; gap:24px; }
            .self-host p { margin:4px 0 0; }
            .text-link { color:var(--green); font-weight:800; }
            .toolbar { display:flex; justify-content:space-between; align-items:end; gap:20px; margin-bottom:34px; }
            .toolbar h1 { font-size:clamp(2.2rem,6vw,4rem); }
            .metrics { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:1px; margin-bottom:34px; border:1px solid var(--line); background:var(--line); }
            .metric { min-height:112px; padding:20px; background:var(--surface); }
            .metric strong { display:block; font:500 2rem Georgia,serif; }
            .metric span { color:var(--muted); font-size:.82rem; font-weight:700; }
            .table-wrap { overflow-x:auto; border-top:1px solid var(--line); }
            table { width:100%; border-collapse:collapse; background:rgba(255,253,247,.55); }
            th,td { padding:14px 12px; border-bottom:1px solid var(--line); text-align:left; white-space:nowrap; }
            th { color:var(--muted); font-size:.72rem; text-transform:uppercase; }
            .status { display:inline-flex; align-items:center; gap:7px; }
            .status::before { content:''; width:8px; height:8px; border-radius:50%; background:#8a918c; }
            .status.running::before { background:#24a36f; }
            .workspace-head { padding-bottom:28px; border-bottom:1px solid var(--line); }
            .workspace-head h1 { overflow-wrap:anywhere; }
            .workspace-meta { display:flex; flex-wrap:wrap; gap:10px 22px; color:var(--muted); }
            .settings { max-width:780px; margin-top:38px; }
            .backup-row { display:flex; align-items:center; justify-content:space-between; gap:16px; padding:14px 0; border-bottom:1px solid var(--line); }
            .backup-row p { margin:3px 0 0; font-size:.86rem; }
            .wake { max-width:700px; padding-top:14vh; }
            .pulse { width:18px; height:18px; margin-bottom:28px; border-radius:50%; background:var(--coral); animation:pulse 1.2s infinite; }
            @keyframes pulse { 50% { transform:scale(1.8); opacity:.25; } }
            [hidden] { display:none !important; }
            @media (max-width:860px) { .plans{grid-template-columns:1fr}.plan{min-height:0} }
            @media (max-width:720px) { main{padding-top:36px}.grid,.fields,.metrics{grid-template-columns:1fr}.self-host,.toolbar{align-items:flex-start;flex-direction:column}.metrics{gap:1px}.metric{min-height:88px} }
          </style>
        </head>
        <body>
          <header><nav><a class="brand" href="/">Help Motivate Me</a><div><a href="/">Create workspace</a>{{adminLink}}</div></nav></header>
          <main>{{body}}</main>
        </body>
        </html>
        """;
      }

    private const string SignupBody = """
        <p class="eyebrow">A private place to keep showing up</p>
        <h1>Your habits. Your own space.</h1>
        <p class="lede">Run everything yourself, start with managed essentials for free, or preview the paid service with managed backups and supplied AI.</p>
        <form id="signup-form">
          <div class="grid plans">
            <label class="plan"><input type="radio" name="plan" value="SelfHosted" checked><strong>Self-hosted</strong><p class="price">$0 · on your server</p><ul><li>Full application and complete data control</li><li>Bring your own OpenAI key</li><li>Manage backups and restores yourself</li></ul></label>
            <label class="plan"><input type="radio" name="plan" value="Free"><strong>Free</strong><p class="price">$0 · managed</p><ul><li>Core habit and identity tools</li><li>Bring your own OpenAI key, optionally</li><li>No managed backups</li></ul></label>
            <label class="plan"><input type="radio" name="plan" value="Paid"><strong>Paid</strong><p class="price">Preview entitlement</p><ul><li>Everything in Free</li><li>Managed backups and restore</li><li>Optional supplied AI</li></ul></label>
          </div>
          <section id="hosted-fields" class="form-section" hidden>
            <h2>Create your workspace</h2>
            <div class="fields">
              <label class="field">Email<input name="ownerEmail" type="email" autocomplete="email" required></label>
              <label class="field">Workspace name<input name="subdomain" pattern="[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?" placeholder="my-habits" required></label>
              <label class="field">Username<input name="username" autocomplete="username" required></label>
              <label class="field">Password<input name="password" type="password" autocomplete="new-password" required></label>
            </div>
          </section>
          <section id="ai-section" class="form-section" hidden>
            <label class="check"><input id="enable-ai" type="checkbox"><span id="ai-choice-label">Use my own OpenAI key</span></label>
            <div id="ai-panel" class="ai-panel" hidden>
              <p id="ai-copy">Your key is passed directly to your isolated workspace and is not stored in control-plane metadata.</p>
              <div class="fields">
                <label class="field">Vendor<select disabled><option>OpenAI</option></select></label>
                <label class="field">Model<select disabled><option>gpt-4.1-mini</option></select></label>
                <label id="api-key-field" class="field">OpenAI API key<input name="aiApiKey" type="password" autocomplete="off"></label>
              </div>
            </div>
          </section>
          <div class="submit-row"><button id="submit" type="submit">View self-hosting guide</button><p id="message" class="message" role="status"></p></div>
        </form>
        <section class="self-host"><div><h2>Run it on your own server</h2><p>The full tenant application is free to self-host. You control the data, backups, and AI key.</p></div><a class="text-link" href="https://github.com/TopSwagCode/help-motivate-me">Self-hosting repository</a></section>
        <script>
          const form = document.querySelector('#signup-form');
          const aiToggle = document.querySelector('#enable-ai');
          const aiPanel = document.querySelector('#ai-panel');
          const apiKeyField = document.querySelector('#api-key-field');
          const message = document.querySelector('#message');
          function selectedPlan() { return new FormData(form).get('plan'); }
          function syncAi() {
            const plan = selectedPlan(); const selfHosted = plan === 'SelfHosted'; const paid = plan === 'Paid';
            document.querySelector('#hosted-fields').hidden = selfHosted;
            document.querySelector('#ai-section').hidden = selfHosted;
            document.querySelector('#submit').textContent = selfHosted ? 'View self-hosting guide' : 'Create workspace';
            document.querySelector('#ai-choice-label').textContent = paid ? 'Include supplied AI' : 'Use my own OpenAI key';
            document.querySelector('#ai-copy').textContent = paid ? 'AI usage is supplied by the hosted service when configured by the operator.' : 'Your key is passed directly to your isolated workspace and is not stored in control-plane metadata.';
            apiKeyField.hidden = paid;
            aiPanel.hidden = !aiToggle.checked;
          }
          form.addEventListener('change', syncAi);
          form.addEventListener('submit', async event => {
            event.preventDefault();
            if (selectedPlan() === 'SelfHosted') { location.assign('https://github.com/TopSwagCode/help-motivate-me'); return; }
            message.textContent = 'Preparing your workspace...';
            const button = document.querySelector('#submit'); button.disabled = true;
            const data = Object.fromEntries(new FormData(form));
            const payload = { ownerEmail:data.ownerEmail, subdomain:data.subdomain, username:data.username, password:data.password, plan:data.plan, enableAi:aiToggle.checked, aiApiKey:data.aiApiKey || null };
            const response = await fetch('/api/tenants', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(payload) });
            const result = await response.json().catch(() => ({}));
            if (response.ok) location.assign(result.configurationUrl);
            else { message.textContent = result.detail || result.message || Object.values(result.errors || {})[0]?.[0] || 'Workspace creation failed.'; button.disabled = false; }
          });
          syncAi();
        </script>
        """;

    private const string WorkspaceBody = """
        <section class="workspace-head">
          <p class="eyebrow">Your workspace</p><h1 id="workspace-name">Loading...</h1>
          <div class="workspace-meta"><span id="type"></span><span id="status"></span><span id="uptime"></span></div>
          <div class="actions"><a id="open-link" class="button" href="#">Open workspace</a><button id="start" type="button">Start</button><button id="stop" class="secondary" type="button">Stop</button><button id="restart" class="danger" type="button">Restart</button></div>
        </section>
        <form id="settings-form" class="settings">
          <h2>Configuration</h2><p>Leave password and API key blank to keep their current values. Saving restarts the container but preserves workspace data.</p>
          <div class="fields"><label class="field">Username<input id="username" name="username" required></label><label class="field">New password<input name="password" type="password" autocomplete="new-password"></label></div>
          <section class="form-section"><label class="check"><input id="enable-ai" type="checkbox"><span id="ai-label">Enable AI</span></label><div id="ai-settings" class="ai-panel" hidden><div class="fields"><label class="field">Vendor<select disabled><option>OpenAI</option></select></label><label class="field">Model<select disabled><option>gpt-4.1-mini</option></select></label><label id="key-field" class="field">New OpenAI API key<input name="aiApiKey" type="password" autocomplete="off"></label></div></div></section>
          <div class="submit-row"><button type="submit">Save and restart</button><p id="message" class="message" role="status"></p></div>
        </form>
        <section id="backups" class="settings" hidden>
          <div class="toolbar"><div><h2>Managed backups</h2><p>Backups briefly stop the workspace for a consistent SQLite and uploads snapshot.</p></div><button id="create-backup" type="button">Create backup</button></div>
          <div id="backup-list"></div>
        </section>
        <script>
          const id = 'TENANT_ID'; let tenant;
          const message = document.querySelector('#message'); const aiToggle = document.querySelector('#enable-ai');
          function duration(seconds) { const hours=Math.floor(seconds/3600), minutes=Math.floor((seconds%3600)/60); return hours ? `${hours}h ${minutes}m estimated uptime` : `${minutes}m estimated uptime`; }
          function syncAi() { document.querySelector('#ai-settings').hidden=!aiToggle.checked; document.querySelector('#key-field').hidden=tenant?.plan==='Paid'; }
          async function loadBackups() {
            if (tenant.plan !== 'Paid') return;
            document.querySelector('#backups').hidden=false;
            const response=await fetch(`/api/tenants/${id}/backups`);if(!response.ok){message.textContent='Could not load backups.';return;}const backups=await response.json();
            const rows=backups.map(backup=>{const row=document.createElement('div');row.className='backup-row';const detail=document.createElement('div');const strong=document.createElement('strong');strong.textContent=new Date(backup.createdUtc).toLocaleString();const size=document.createElement('p');size.textContent=`${Math.max(1,Math.round(backup.sizeBytes/1024))} KB`;detail.append(strong,size);const button=document.createElement('button');button.type='button';button.className='secondary';button.textContent='Restore';button.addEventListener('click',()=>restoreBackup(backup.id));row.append(detail,button);return row;});document.querySelector('#backup-list').replaceChildren(...rows);if(!rows.length){const empty=document.createElement('p');empty.textContent='No backups yet.';document.querySelector('#backup-list').append(empty);}
          }
          async function load() {
            const response=await fetch(`/api/tenants/${id}`); if(!response.ok){ message.textContent='Workspace not found.'; return; } tenant=await response.json();
            document.querySelector('#workspace-name').textContent=`${tenant.subdomain}.localhost`; document.querySelector('#type').textContent=tenant.workspaceType; document.querySelector('#status').textContent=tenant.status; document.querySelector('#uptime').textContent=duration(tenant.estimatedUptimeSeconds); document.querySelector('#open-link').href=tenant.url; document.querySelector('#username').value=tenant.username; aiToggle.checked=tenant.aiMode!=='Disabled'; document.querySelector('#ai-label').textContent=tenant.plan==='Paid'?'Include supplied AI':'Use my own OpenAI key'; syncAi(); await loadBackups();
          }
          async function act(action) { message.textContent=`${action[0].toUpperCase()+action.slice(1)}ing workspace...`; const response=await fetch(`/api/tenants/${id}/${action}`,{method:'POST'}); message.textContent=response.ok?'Done.':'Operation failed.'; await load(); }
          for(const action of ['start','stop','restart']) document.querySelector(`#${action}`).addEventListener('click',()=>act(action));
          document.querySelector('#create-backup').addEventListener('click',async()=>{message.textContent='Creating backup...';const response=await fetch(`/api/tenants/${id}/backups`,{method:'POST'});message.textContent=response.ok?'Backup created.':'Backup failed.';await load();});
          async function restoreBackup(backupId){if(!confirm('Restore this backup? Current workspace data will be replaced.'))return;message.textContent='Restoring backup...';const response=await fetch(`/api/tenants/${id}/backups/${backupId}/restore`,{method:'POST'});message.textContent=response.ok?'Backup restored.':'Restore failed.';await load();}
          aiToggle.addEventListener('change',syncAi);
          document.querySelector('#settings-form').addEventListener('submit',async event=>{ event.preventDefault(); message.textContent='Applying configuration...'; const data=Object.fromEntries(new FormData(event.currentTarget)); const response=await fetch(`/api/tenants/${id}/settings`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:data.username,password:data.password||null,enableAi:aiToggle.checked,aiApiKey:data.aiApiKey||null})}); const result=await response.json().catch(()=>({})); message.textContent=response.ok?'Configuration updated.':result.detail||Object.values(result.errors||{})[0]?.[0]||'Configuration failed.'; if(response.ok){ event.currentTarget.password.value=''; event.currentTarget.aiApiKey.value=''; await load(); } });
          load();
        </script>
        """;

    private const string AdminBody = """
        <div class="toolbar"><div><p class="eyebrow">Local control plane</p><h1>Workspace administration</h1><p>Running workspaces stop after IDLE_TIMEOUT minutes without tenant traffic. Idle state is checked every IDLE_SWEEP seconds.</p></div><button id="refresh" type="button">Refresh</button></div>
        <section class="metrics"><div class="metric"><strong id="total">—</strong><span>Total workspaces</span></div><div class="metric"><strong id="free">—</strong><span>Free</span></div><div class="metric"><strong id="free-ai">—</strong><span>Free + AI config</span></div><div class="metric"><strong id="paid">—</strong><span>Paid</span></div><div class="metric"><strong id="running">—</strong><span>Running</span></div><div class="metric"><strong id="stopped">—</strong><span>Stopped</span></div></section>
        <div class="table-wrap"><table><thead><tr><th>Workspace</th><th>Owner</th><th>Type</th><th>State</th><th>Last activity</th><th>Last login</th><th>Current run</th><th>Total uptime</th><th></th></tr></thead><tbody id="rows"></tbody></table></div>
        <p id="empty" hidden>No workspaces have been created.</p>
        <script>
          function duration(seconds){const days=Math.floor(seconds/86400),hours=Math.floor((seconds%86400)/3600),minutes=Math.floor((seconds%3600)/60);return days?`${days}d ${hours}h`:hours?`${hours}h ${minutes}m`:`${minutes}m`;}
          function currentRun(tenant){return tenant.status==='Running'&&tenant.lastStartedUtc?duration(Math.max(0,(Date.now()-new Date(tenant.lastStartedUtc).getTime())/1000)):'—';}
          function cell(text){const td=document.createElement('td');td.textContent=text;return td;}
          async function load(){const tenants=await fetch('/api/tenants').then(r=>r.json()); const count=type=>tenants.filter(t=>t.workspaceType===type).length; document.querySelector('#total').textContent=tenants.length;document.querySelector('#free').textContent=count('Free');document.querySelector('#free-ai').textContent=count('Free+AI config');document.querySelector('#paid').textContent=count('Paid');document.querySelector('#running').textContent=tenants.filter(t=>t.status==='Running').length;document.querySelector('#stopped').textContent=tenants.filter(t=>t.status==='Stopped').length; const rows=tenants.map(t=>{const tr=document.createElement('tr');const workspace=cell(t.subdomain);const link=document.createElement('a');link.className='text-link';link.href=t.url;link.textContent=t.subdomain;workspace.replaceChildren(link);const status=cell(t.status);status.className=`status ${t.status.toLowerCase()}`;const manage=cell('');const manageLink=document.createElement('a');manageLink.className='text-link';manageLink.href=t.configurationUrl;manageLink.textContent='Manage';manage.append(manageLink);tr.append(workspace,cell(t.ownerEmail||'—'),cell(t.workspaceType),status,cell(new Date(t.lastRequestUtc).toLocaleString()),cell(t.lastLoginUtc?new Date(t.lastLoginUtc).toLocaleString():'Never'),cell(currentRun(t)),cell(duration(t.estimatedUptimeSeconds)),manage);return tr;});document.querySelector('#rows').replaceChildren(...rows);document.querySelector('#empty').hidden=tenants.length>0;}
          document.querySelector('#refresh').addEventListener('click',load);load();
        </script>
        """;
}