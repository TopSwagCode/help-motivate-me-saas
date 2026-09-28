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
      WritePageAsync(context, "Habit Space administration",
        AdminBody
          .Replace("IDLE_TIMEOUT", options.Value.IdleTimeoutMinutes.ToString())
          .Replace("IDLE_SWEEP", options.Value.IdleSweepSeconds.ToString()),
        cancellationToken);

    public static Task WriteWorkspaceAsync(Guid id, HttpContext context, CancellationToken cancellationToken) =>
        WritePageAsync(context, "Habit Space settings", WorkspaceBody.Replace("TENANT_ID", id.ToString()),
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
            await WritePageAsync(context, "Habit Space not found",
              $"<section class=\"wake\"><h1>Habit Space not found</h1><p><strong>{WebUtility.HtmlEncode(host)}</strong> is not a provisioned Habit Space.</p><a class=\"text-link\" href=\"http://saas.localhost:8088\">Create a Habit Space</a></section>",
                cancellationToken);
            return;
        }

        startQueue.Enqueue(tenant.Id);
        await WritePageAsync(context, "Starting Habit Space",
          $"<meta http-equiv=\"refresh\" content=\"2\"><section class=\"wake\"><div class=\"pulse\"></div><p class=\"eyebrow\">Waking your Habit Space</p><h1>Starting {WebUtility.HtmlEncode(tenant.AppName)}</h1><p>Your data is safe. This page will continue automatically when your Habit Space is healthy.</p></section>",
            cancellationToken);
    }

    private static Task WriteSignupAsync(HttpContext context, CancellationToken cancellationToken) =>
        WritePageAsync(context, "Create your Habit Space", SignupBody, cancellationToken);

    private static Task WritePageAsync(HttpContext context, string title, string body,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(Page(title, body), cancellationToken);
    }

    private static string Page(string title, string body)
    {
      var adminLink = title == "Habit Space administration" ? "<a href=\"/admin\">Admin</a>" : string.Empty;
      var createWorkspaceLink = title == "Starting Habit Space"
        ? string.Empty
        : "<a class=\"create-link\" href=\"/\">Create Habit Space</a>";
      return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="theme-color" content="#d4944c">
          <title>{{WebUtility.HtmlEncode(title)}} · Help Motivate Me</title>
          <link rel="icon" type="image/png" href="/favicon-32x32.png">
          <link rel="preconnect" href="https://fonts.googleapis.com">
          <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
          <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Nunito:wght@400;500;600;700;800&display=swap" rel="stylesheet">
          <style>
            :root { color-scheme:light; --primary-50:#fefcf9; --primary-100:#fdf6ed; --primary-200:#f9e8d2; --primary-300:#f2d4ae; --primary-500:#d4944c; --primary-600:#b87a3a; --primary-700:#965e2c; --warm-cream:#faf7f2; --warm-beige:#f5f0e8; --warm-paper:#fdfcfa; --sage-500:#768862; --sage-600:#5d6d4e; --cocoa-400:#b09780; --cocoa-500:#9a7d64; --cocoa-600:#8a6a54; --cocoa-700:#735748; --cocoa-800:#5f483d; --cocoa-900:#4a3830; --danger:#dc2626; }
            * { box-sizing:border-box; }
            html { min-height:100%; background:var(--warm-cream); }
            body { margin:0; min-height:100vh; color:var(--cocoa-800); background:linear-gradient(180deg,var(--warm-paper) 0,var(--warm-cream) 240px,var(--warm-cream) 100%); font-family:'Nunito','Inter',system-ui,sans-serif; letter-spacing:0; -webkit-font-smoothing:antialiased; }
            header { position:sticky; top:0; z-index:10; border-radius:0 0 24px 24px; background:linear-gradient(135deg,var(--warm-paper) 0%,var(--warm-cream) 50%,var(--primary-200) 100%); box-shadow:0 10px 25px rgba(212,148,76,.12); }
            nav { width:min(1280px,100%); height:56px; margin:auto; padding:0 32px; display:flex; align-items:center; justify-content:space-between; gap:20px; }
            .brand { height:56px; margin-left:24px; display:flex; align-items:center; gap:12px; color:var(--primary-600); font-size:18px; font-weight:700; text-decoration:none; }
            .brand-mascot { position:relative; width:96px; height:96px; flex:none; }
            .brand-mascot img { position:absolute; right:0; bottom:16px; width:100%; height:auto; display:block; }
            nav div { display:flex; align-items:center; gap:8px; }
            nav div a { min-height:40px; padding:0 12px; display:inline-flex; align-items:center; border-radius:16px; color:var(--cocoa-700); font-size:.88rem; font-weight:700; text-decoration:none; transition:background-color .2s,color .2s; }
            nav div a:hover { color:var(--primary-700); background:var(--primary-100); }
            main { width:min(1120px,calc(100% - 32px)); margin:auto; padding:48px 0 80px; }
            h1,h2,h3 { color:var(--cocoa-900); font-family:'Nunito','Inter',system-ui,sans-serif; letter-spacing:0; }
            h1 { max-width:760px; margin:8px 0 16px; font-size:2.75rem; line-height:1.08; font-weight:800; }
            h2 { margin:0 0 8px; font-size:1.5rem; font-weight:700; }
            h3 { margin:0 0 8px; font-size:1.15rem; font-weight:700; }
            p { color:var(--cocoa-600); line-height:1.6; }
            .lede { max-width:690px; font-size:1.08rem; }
            .eyebrow { margin:0; color:var(--primary-600); font-size:.76rem; font-weight:800; text-transform:uppercase; }
            .grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:16px; }
            .plans { grid-template-columns:repeat(3,minmax(0,1fr)); margin:40px 0 32px; }
            .plan { position:relative; display:block; min-height:230px; padding:24px; border:1px solid var(--primary-200); border-radius:24px; background:var(--warm-paper); box-shadow:0 1px 3px rgba(95,72,61,.06); cursor:pointer; transition:border-color .2s,box-shadow .2s,transform .2s; }
            .plan:hover { border-color:var(--primary-300); box-shadow:0 10px 24px rgba(95,72,61,.08); transform:translateY(-2px); }
            .plan:has(input:checked) { border:2px solid var(--primary-500); padding:23px; background:var(--primary-50); box-shadow:0 10px 30px rgba(212,148,76,.16); }
            .plan input { position:absolute; top:22px; right:22px; width:19px; height:19px; accent-color:var(--primary-500); }
            .plan strong { display:block; color:var(--cocoa-900); font-size:1.4rem; font-weight:800; }
            .price { margin:8px 0 18px; color:var(--cocoa-800); font-weight:800; }
            .plan ul { margin:0; padding-left:20px; color:var(--cocoa-600); line-height:1.8; }
            .form-section { margin-top:28px; padding-top:28px; border-top:1px solid var(--primary-200); }
            .fields { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:18px; margin-top:20px; }
            label.field { display:grid; gap:7px; color:var(--cocoa-700); font-size:.88rem; font-weight:700; }
            input,select { width:100%; min-height:46px; border:1px solid var(--primary-200); border-radius:16px; background:var(--warm-paper); padding:10px 13px; color:var(--cocoa-800); font:1rem 'Nunito','Inter',system-ui,sans-serif; box-shadow:0 1px 2px rgba(95,72,61,.04); transition:border-color .2s,box-shadow .2s; }
            input:focus,select:focus { border-color:var(--primary-500); box-shadow:0 0 0 3px rgba(212,148,76,.18); outline:0; }
            button:focus-visible,a:focus-visible { outline:3px solid rgba(212,148,76,.32); outline-offset:2px; }
            .check { display:flex; align-items:flex-start; gap:10px; font-weight:700; }
            .check input { width:19px; min-height:19px; margin-top:2px; accent-color:var(--primary-500); }
            .ai-panel { margin-top:18px; padding:20px; border:1px solid var(--primary-200); border-radius:16px; background:var(--primary-50); }
            .ai-panel .fields { margin-top:16px; }
            .applied-settings { margin-top:24px; padding:20px 0; border-top:1px solid var(--primary-200); border-bottom:1px solid var(--primary-200); }
            .applied-settings ul { margin:12px 0 0; padding-left:20px; color:var(--cocoa-600); line-height:1.75; }
            button,.button { min-height:44px; border:1px solid transparent; border-radius:16px; padding:0 18px; display:inline-flex; align-items:center; justify-content:center; color:white; background:var(--primary-500); font:700 .9rem 'Nunito','Inter',system-ui,sans-serif; text-decoration:none; cursor:pointer; box-shadow:0 1px 3px rgba(95,72,61,.12); transition:background-color .2s,box-shadow .2s,transform .2s; }
            button:hover,.button:hover { background:var(--primary-600); box-shadow:0 5px 14px rgba(184,122,58,.2); transform:translateY(-1px); }
            button.secondary { border-color:var(--primary-200); color:var(--cocoa-700); background:var(--warm-paper); }
            button.secondary:hover { background:var(--primary-50); }
            button.danger { background:var(--danger); }
            button:disabled { cursor:wait; opacity:.6; }
            .submit-row,.actions { display:flex; align-items:center; flex-wrap:wrap; gap:10px; margin-top:24px; }
            .message { min-height:24px; color:var(--danger); font-weight:700; }
            .self-host { margin-top:44px; padding:28px; border:1px solid var(--primary-200); border-radius:24px; display:flex; justify-content:space-between; align-items:center; gap:24px; background:var(--warm-paper); }
            .self-host p { margin:4px 0 0; }
            .text-link { color:var(--primary-600); font-weight:800; }
            .text-link:hover { color:var(--primary-700); }
            .toolbar { display:flex; justify-content:space-between; align-items:end; gap:20px; margin-bottom:34px; }
            .toolbar h1 { font-size:2.5rem; }
            .toolbar p:not(.eyebrow) { max-width:720px; }
            .metrics { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:12px; margin-bottom:34px; }
            .metric { min-height:112px; padding:20px; border:1px solid var(--primary-200); border-radius:20px; background:var(--warm-paper); box-shadow:0 1px 3px rgba(95,72,61,.06); }
            .metric strong { display:block; color:var(--cocoa-900); font-size:2rem; font-weight:800; }
            .metric span { color:var(--cocoa-500); font-size:.82rem; font-weight:700; }
            .deployment-panel { margin-bottom:34px; padding:24px; border:1px solid var(--primary-200); border-radius:20px; display:grid; grid-template-columns:1fr auto; align-items:center; gap:20px; background:var(--warm-paper); box-shadow:0 1px 3px rgba(95,72,61,.06); }
            .deployment-panel h2,.deployment-panel p { margin:0; }
            .deployment-panel code { color:var(--cocoa-800); overflow-wrap:anywhere; }
            .deployment-status { grid-column:1/-1; padding-top:16px; border-top:1px solid var(--primary-100); }
            progress { width:100%; height:10px; margin-top:10px; accent-color:var(--sage-500); }
            .table-wrap { overflow-x:auto; border:1px solid var(--primary-200); border-radius:20px; background:var(--warm-paper); box-shadow:0 1px 3px rgba(95,72,61,.06); }
            table { width:100%; border-collapse:collapse; }
            th,td { padding:14px 12px; border-bottom:1px solid var(--primary-100); text-align:left; white-space:nowrap; }
            tbody tr:last-child td { border-bottom:0; }
            tbody tr:hover { background:var(--primary-50); }
            th { color:var(--cocoa-500); background:var(--warm-beige); font-size:.72rem; text-transform:uppercase; }
            .status { display:inline-flex; align-items:center; gap:7px; padding:5px 9px; border-radius:999px; background:var(--warm-beige); }
            .status::before { content:''; width:8px; height:8px; border-radius:50%; background:#8a918c; }
            .status.running { color:var(--sage-600); background:#f0f4eb; }
            .status.running::before { background:var(--sage-500); }
            .workspace-head { padding:28px; border:1px solid var(--primary-200); border-radius:24px; background:var(--warm-paper); box-shadow:0 1px 3px rgba(95,72,61,.06); }
            .workspace-head h1 { overflow-wrap:anywhere; }
            .workspace-meta { display:flex; flex-wrap:wrap; gap:10px 22px; color:var(--cocoa-500); }
            .settings { max-width:780px; margin-top:24px; padding:28px; border:1px solid var(--primary-200); border-radius:24px; background:var(--warm-paper); box-shadow:0 1px 3px rgba(95,72,61,.06); }
            .backup-row { display:flex; align-items:center; justify-content:space-between; gap:16px; padding:14px 0; border-bottom:1px solid var(--primary-100); }
            .backup-row p { margin:3px 0 0; font-size:.86rem; }
            .wake { max-width:700px; padding-top:14vh; }
            .pulse { width:18px; height:18px; margin-bottom:28px; border-radius:50%; background:var(--primary-500); animation:pulse 1.2s infinite; }
            @keyframes pulse { 50% { transform:scale(1.8); opacity:.25; } }
            [hidden] { display:none !important; }
            @media (max-width:860px) { .plans{grid-template-columns:1fr}.plan{min-height:0} }
            @media (max-width:720px) { nav{height:48px;padding:0 12px}.brand{height:48px;margin-left:16px;gap:8px;font-size:16px}.brand-mascot{width:80px;height:80px}.brand-mascot img{bottom:14px}nav div a{padding:0 8px;font-size:.8rem}.create-link{display:none}main{width:calc(100% - 24px);padding-top:32px}h1,.toolbar h1{font-size:2rem}.grid,.fields,.metrics{grid-template-columns:1fr}.self-host,.toolbar{align-items:flex-start;flex-direction:column}.deployment-panel{grid-template-columns:1fr}.deployment-status{grid-column:1}.metric{min-height:88px}.workspace-head,.settings,.self-host{padding:20px;border-radius:20px} }
          </style>
        </head>
        <body>
          <header><nav><a class="brand" href="/"><span class="brand-mascot"><img src="/appbarsloth.png" alt=""></span><span>HelpMotivateMe</span></a><div>{{createWorkspaceLink}}{{adminLink}}</div></nav></header>
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
            <h2>Create your Habit Space</h2>
            <div class="fields">
              <label class="field">Email<input name="ownerEmail" type="email" autocomplete="email" required></label>
              <label class="field">Habit Space name<input name="appName" maxlength="30" placeholder="My Habit Space" required></label>
              <label class="field">Web address<input name="subdomain" pattern="[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?" placeholder="my-habits" required></label>
              <label class="field">Username<input name="username" autocomplete="username" required></label>
              <label class="field">Password<input name="password" type="password" autocomplete="new-password" required></label>
            </div>
            <div class="applied-settings">
              <h3>What we configure</h3>
              <ul>
                <li>The installed app uses your Habit Space name and opens directly to your daily view.</li>
                <li>A private notification keypair is generated automatically and kept in your isolated data volume.</li>
                <li>Notifications remain off until you allow them on each device.</li>
                <li>Your web address cannot be changed after creation; your app name and login settings can.</li>
              </ul>
            </div>
          </section>
          <section id="ai-section" class="form-section" hidden>
            <label class="check"><input id="enable-ai" type="checkbox"><span id="ai-choice-label">Use my own OpenAI key</span></label>
            <div id="ai-panel" class="ai-panel" hidden>
              <p id="ai-copy">Your key is passed directly to your isolated Habit Space and is not stored in control-plane metadata.</p>
              <div class="fields">
                <label class="field">Vendor<select disabled><option>OpenAI</option></select></label>
                <label class="field">Model<select disabled><option>gpt-4.1-mini</option></select></label>
                <label id="api-key-field" class="field">OpenAI API key<input name="aiApiKey" type="password" autocomplete="off"></label>
              </div>
            </div>
          </section>
          <div class="submit-row"><button id="submit" type="submit">View self-hosting guide</button><p id="message" class="message" role="status"></p></div>
        </form>
        <section class="self-host"><div><h2>Run it on your own server</h2><p>The full app is free to self-host. Its name is configurable, and notification keys are generated automatically beside your persistent data.</p></div><a class="text-link" href="https://github.com/TopSwagCode/help-motivate-me">Self-hosting repository</a></section>
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
            document.querySelector('#submit').textContent = selfHosted ? 'View self-hosting guide' : 'Create Habit Space';
            document.querySelector('#ai-choice-label').textContent = paid ? 'Include supplied AI' : 'Use my own OpenAI key';
            document.querySelector('#ai-copy').textContent = paid ? 'AI usage is supplied by the hosted service when configured by the operator.' : 'Your key is passed directly to your isolated Habit Space and is not stored in control-plane metadata.';
            apiKeyField.hidden = paid;
            aiPanel.hidden = !aiToggle.checked;
          }
          form.addEventListener('change', syncAi);
          form.addEventListener('submit', async event => {
            event.preventDefault();
            if (selectedPlan() === 'SelfHosted') { location.assign('https://github.com/TopSwagCode/help-motivate-me'); return; }
            message.textContent = 'Preparing your Habit Space...';
            const button = document.querySelector('#submit'); button.disabled = true;
            const data = Object.fromEntries(new FormData(form));
            const payload = { ownerEmail:data.ownerEmail, subdomain:data.subdomain, appName:data.appName, username:data.username, password:data.password, plan:data.plan, enableAi:aiToggle.checked, aiApiKey:data.aiApiKey || null };
            const response = await fetch('/api/tenants', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(payload) });
            const result = await response.json().catch(() => ({}));
            if (response.ok) location.assign(result.configurationUrl);
            else { message.textContent = result.detail || result.message || Object.values(result.errors || {})[0]?.[0] || 'Habit Space creation failed.'; button.disabled = false; }
          });
          syncAi();
        </script>
        """;

    private const string WorkspaceBody = """
        <section class="workspace-head">
          <p class="eyebrow">Your Habit Space</p><h1 id="workspace-name">Loading...</h1>
          <div class="workspace-meta"><span id="type"></span><span id="status"></span><span id="uptime"></span></div>
          <div class="actions"><a id="open-link" class="button" href="#">Open Habit Space</a><button id="start" type="button">Start</button><button id="stop" class="secondary" type="button">Stop</button><button id="restart" class="danger" type="button">Restart</button></div>
        </section>
        <form id="settings-form" class="settings">
          <h2>Configuration</h2><p>The Habit Space name is also used by the installed app. Leave password and API key blank to keep their current values. Saving restarts the container but preserves your data and notification keys.</p>
          <div class="fields"><label class="field">Habit Space name<input id="app-name" name="appName" maxlength="30" required></label><label class="field">Username<input id="username" name="username" required></label><label class="field">New password<input name="password" type="password" autocomplete="new-password"></label></div>
          <section class="form-section"><label class="check"><input id="enable-ai" type="checkbox"><span id="ai-label">Enable AI</span></label><div id="ai-settings" class="ai-panel" hidden><div class="fields"><label class="field">Vendor<select disabled><option>OpenAI</option></select></label><label class="field">Model<select disabled><option>gpt-4.1-mini</option></select></label><label id="key-field" class="field">New OpenAI API key<input name="aiApiKey" type="password" autocomplete="off"></label></div></div></section>
          <div class="submit-row"><button type="submit">Save and restart</button><p id="message" class="message" role="status"></p></div>
        </form>
        <section id="backups" class="settings" hidden>
          <div class="toolbar"><div><h2>Managed backups</h2><p>Backups briefly stop the Habit Space for a consistent data snapshot.</p></div><button id="create-backup" type="button">Create backup</button></div>
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
            const response=await fetch(`/api/tenants/${id}`); if(!response.ok){ message.textContent='Habit Space not found.'; return; } tenant=await response.json();
            document.querySelector('#workspace-name').textContent=tenant.appName; document.querySelector('#type').textContent=tenant.workspaceType; document.querySelector('#status').textContent=tenant.status; document.querySelector('#uptime').textContent=duration(tenant.estimatedUptimeSeconds); document.querySelector('#open-link').href=tenant.url; document.querySelector('#app-name').value=tenant.appName; document.querySelector('#username').value=tenant.username; aiToggle.checked=tenant.aiMode!=='Disabled'; document.querySelector('#ai-label').textContent=tenant.plan==='Paid'?'Include supplied AI':'Use my own OpenAI key'; syncAi(); await loadBackups();
          }
          async function act(action) { message.textContent=`${action[0].toUpperCase()+action.slice(1)}ing Habit Space...`; const response=await fetch(`/api/tenants/${id}/${action}`,{method:'POST'}); message.textContent=response.ok?'Done.':'Operation failed.'; await load(); }
          for(const action of ['start','stop','restart']) document.querySelector(`#${action}`).addEventListener('click',()=>act(action));
          document.querySelector('#create-backup').addEventListener('click',async()=>{message.textContent='Creating backup...';const response=await fetch(`/api/tenants/${id}/backups`,{method:'POST'});message.textContent=response.ok?'Backup created.':'Backup failed.';await load();});
          async function restoreBackup(backupId){if(!confirm('Restore this backup? Current Habit Space data will be replaced.'))return;message.textContent='Restoring backup...';const response=await fetch(`/api/tenants/${id}/backups/${backupId}/restore`,{method:'POST'});message.textContent=response.ok?'Backup restored.':'Restore failed.';await load();}
          aiToggle.addEventListener('change',syncAi);
          document.querySelector('#settings-form').addEventListener('submit',async event=>{ event.preventDefault(); message.textContent='Applying configuration...'; const data=Object.fromEntries(new FormData(event.currentTarget)); const response=await fetch(`/api/tenants/${id}/settings`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({appName:data.appName,username:data.username,password:data.password||null,enableAi:aiToggle.checked,aiApiKey:data.aiApiKey||null})}); const result=await response.json().catch(()=>({})); message.textContent=response.ok?'Configuration updated.':result.detail||Object.values(result.errors||{})[0]?.[0]||'Configuration failed.'; if(response.ok){ event.currentTarget.password.value=''; event.currentTarget.aiApiKey.value=''; await load(); } });
          load();
        </script>
        """;

    private const string AdminBody = """
        <div class="toolbar"><div><p class="eyebrow">Local control plane</p><h1>Habit Space administration</h1><p>Running Habit Spaces stop after IDLE_TIMEOUT minutes without tenant traffic. Idle state is checked every IDLE_SWEEP seconds.</p></div><button id="refresh" type="button">Refresh</button></div>
        <section class="deployment-panel">
          <div><p class="eyebrow">Tenant application</p><h2>Rolling deployment</h2><p>Replace Habit Spaces one at a time with the configured tenant image. Deployment stops on the first failed health check.</p></div>
          <button id="deploy-latest" type="button">Deploy latest version</button>
          <div class="deployment-status"><p id="deployment-message" role="status">Checking deployment status...</p><p>Target image: <code id="target-image">—</code></p><progress id="deployment-progress" value="0" max="1"></progress></div>
        </section>
        <section class="metrics"><div class="metric"><strong id="total">—</strong><span>Total Habit Spaces</span></div><div class="metric"><strong id="free">—</strong><span>Free</span></div><div class="metric"><strong id="free-ai">—</strong><span>Free + AI config</span></div><div class="metric"><strong id="paid">—</strong><span>Paid</span></div><div class="metric"><strong id="running">—</strong><span>Running</span></div><div class="metric"><strong id="stopped">—</strong><span>Stopped</span></div></section>
        <div class="table-wrap"><table><thead><tr><th>Habit Space</th><th>Owner</th><th>Type</th><th>Version</th><th>State</th><th>Last activity</th><th>Last login</th><th>Current run</th><th>Total uptime</th><th></th></tr></thead><tbody id="rows"></tbody></table></div>
        <p id="empty" hidden>No Habit Spaces have been created.</p>
        <script>
          function duration(seconds){const days=Math.floor(seconds/86400),hours=Math.floor((seconds%86400)/3600),minutes=Math.floor((seconds%3600)/60);return days?`${days}d ${hours}h`:hours?`${hours}h ${minutes}m`:`${minutes}m`;}
          function currentRun(tenant){return tenant.status==='Running'&&tenant.lastStartedUtc?duration(Math.max(0,(Date.now()-new Date(tenant.lastStartedUtc).getTime())/1000)):'—';}
          function cell(text){const td=document.createElement('td');td.textContent=text;return td;}
          function imageVersion(imageId){return imageId?imageId.replace('sha256:','').slice(0,12):'Unknown';}
          async function loadTenants(){const tenants=await fetch('/api/tenants').then(r=>r.json()); const count=type=>tenants.filter(t=>t.workspaceType===type).length; document.querySelector('#total').textContent=tenants.length;document.querySelector('#free').textContent=count('Free');document.querySelector('#free-ai').textContent=count('Free+AI config');document.querySelector('#paid').textContent=count('Paid');document.querySelector('#running').textContent=tenants.filter(t=>t.status==='Running').length;document.querySelector('#stopped').textContent=tenants.filter(t=>t.status==='Stopped').length; const rows=tenants.map(t=>{const tr=document.createElement('tr');const workspace=cell(t.appName);const link=document.createElement('a');link.className='text-link';link.href=t.url;link.textContent=t.appName;workspace.replaceChildren(link);const status=cell(t.status);status.className=`status ${t.status.toLowerCase()}`;const version=cell(imageVersion(t.currentImageId));version.title=t.currentImageId||'Image unavailable';const manage=cell('');const manageLink=document.createElement('a');manageLink.className='text-link';manageLink.href=t.configurationUrl;manageLink.textContent='Manage';manage.append(manageLink);tr.append(workspace,cell(t.ownerEmail||'—'),cell(t.workspaceType),version,status,cell(new Date(t.lastRequestUtc).toLocaleString()),cell(t.lastLoginUtc?new Date(t.lastLoginUtc).toLocaleString():'Never'),cell(currentRun(t)),cell(duration(t.estimatedUptimeSeconds)),manage);return tr;});document.querySelector('#rows').replaceChildren(...rows);document.querySelector('#empty').hidden=tenants.length>0;}
          async function loadDeployment(){const deployment=await fetch('/api/admin/deployments/latest').then(r=>r.json());const active=deployment.state==='Queued'||deployment.state==='Running';const button=document.querySelector('#deploy-latest');button.disabled=active;button.textContent=active?'Deployment running...':'Deploy latest version';document.querySelector('#target-image').textContent=deployment.targetImage||'Configured when deployment starts';const progress=document.querySelector('#deployment-progress');progress.max=Math.max(1,deployment.total);progress.value=deployment.completed;const detail=deployment.currentTenant?` Current: ${deployment.currentTenant}.`:'';const messages={Idle:'No deployment has run since the control plane started.',Queued:`Queued ${deployment.total} Habit Spaces.`,Running:`Updated ${deployment.completed} of ${deployment.total}.${detail}`,Completed:`Deployment complete. Updated ${deployment.completed} Habit Spaces.`,Failed:`Deployment stopped at ${deployment.currentTenant}: ${deployment.error}`};document.querySelector('#deployment-message').textContent=messages[deployment.state]||deployment.state;if(active)setTimeout(loadDeployment,1500);else await loadTenants();}
          async function deployLatest(){if(!confirm('Deploy the configured tenant image to every Habit Space, one at a time?'))return;const button=document.querySelector('#deploy-latest');button.disabled=true;const response=await fetch('/api/admin/deployments/latest',{method:'POST'});const result=await response.json().catch(()=>({}));if(!response.ok){document.querySelector('#deployment-message').textContent=result.message||'Deployment could not start.';}await loadDeployment();}
          async function load(){await Promise.all([loadTenants(),loadDeployment()]);}
          document.querySelector('#deploy-latest').addEventListener('click',deployLatest);document.querySelector('#refresh').addEventListener('click',load);load();
        </script>
        """;
}