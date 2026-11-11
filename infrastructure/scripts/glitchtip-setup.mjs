#!/usr/bin/env node
// `make errors-setup` (ADR-0005): prepares the local GlitchTip for development and prints the DSNs for .env.
// Idempotent: creates (or keeps) the admin user, the "gameverse" org and team, the api / web / unity projects and an API
// token used by verification scripts. Runs GlitchTip's own Django shell inside its container, so it does not depend on
// GlitchTip's login flow. Reads GLITCHTIP_ADMIN_EMAIL / GLITCHTIP_ADMIN_PASSWORD from .env.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const env = Object.fromEntries(
  readFileSync('.env', 'utf8')
    .split(/\r?\n/)
    .filter((l) => /^[A-Z0-9_]+=/.test(l))
    .map((l) => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]),
);
const email = env.GLITCHTIP_ADMIN_EMAIL;
const password = env.GLITCHTIP_ADMIN_PASSWORD;
if (!email || !password) {
  console.error('Set GLITCHTIP_ADMIN_EMAIL and GLITCHTIP_ADMIN_PASSWORD in .env (see .env.example), then `make errors-up`.');
  process.exit(2);
}

// Values go in as JSON, so nothing from .env is ever interpreted as Python code.
const python = `
import json
from django.db import transaction
from apps.users.models import User
from apps.organizations_ext.models import Organization, OrganizationUser
from apps.teams.models import Team
from apps.projects.models import Project, ProjectKey
from apps.api_tokens.models import APIToken

cfg = json.loads(${JSON.stringify(JSON.stringify({ email, password }))})
with transaction.atomic():
    user, _ = User.objects.get_or_create(email=cfg["email"])
    user.is_superuser = True
    user.is_staff = True
    user.set_password(cfg["password"])
    user.save()

    org = Organization.objects.filter(slug="gameverse").first()
    if org is None:
        org = Organization.objects.create(name="GameVerse", slug="gameverse")
    if not OrganizationUser.objects.filter(organization=org, user=user).exists():
        org.add_user(user, role=3)  # 3 = Owner
    team, _ = Team.objects.get_or_create(organization=org, slug="gameverse")

    dsns = {}
    for slug, platform in (("api", "csharp-aspnetcore"), ("web", "javascript-react"), ("unity", "csharp")):
        project = Project.objects.filter(organization=org, slug=slug).first()
        if project is None:
            project = Project.objects.create(organization=org, name=slug, slug=slug, platform=platform)
        project.teams.add(team)
        key = ProjectKey.objects.filter(project=project).first() or ProjectKey.objects.create(project=project)
        dsns[slug] = key.get_dsn()

    token = APIToken.objects.filter(user=user, label="gameverse-dev").first()
    if token is None:
        token = APIToken.objects.create(user=user, label="gameverse-dev")
        token.scopes = sum(1 << i for i in range(len(APIToken._meta.get_field("scopes").flags)))
        token.save()

print("GV_GLITCHTIP " + json.dumps({"dsns": dsns, "token": token.token}))
`;

let out;
try {
  out = execFileSync('docker', ['compose', '--profile', 'errors', 'exec', '-T', 'glitchtip', './manage.py', 'shell'], {
    input: python,
    encoding: 'utf8',
    stdio: ['pipe', 'pipe', 'pipe'],
  });
} catch (error) {
  console.error('GlitchTip setup failed. Is it running (`make errors-up`)?\n' + (error.stderr || error.message));
  console.error('Manual fallback: README.md, "Error reporting".');
  process.exit(1);
}

const line = out.split(/\r?\n/).find((l) => l.startsWith('GV_GLITCHTIP '));
if (!line) {
  console.error('GlitchTip did not report its DSNs. Output:\n' + out);
  process.exit(1);
}
const { dsns, token } = JSON.parse(line.slice('GV_GLITCHTIP '.length));
// The API container reaches GlitchTip by its compose service name, not the host port.
const apiDsn = dsns.api.replace(/@[^/]+\//, '@glitchtip:8000/');

console.log(`GlitchTip ready at http://127.0.0.1:${env.GLITCHTIP_PORT || 8000} (login: ${email}).`);
console.log('Paste into .env:\n');
console.log(`GAMEVERSE_ERROR_DSN_API=${apiDsn}`);
console.log(`VITE_ERROR_DSN=${dsns.web}`);
console.log(`UNITY_ERROR_DSN=${dsns.unity}`);
console.log(`GLITCHTIP_API_TOKEN=${token}`);
console.log(`\n(For \`dotnet run\` on the host, use the api DSN with 127.0.0.1:8000: ${dsns.api})`);
