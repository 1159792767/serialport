"""Public GitHub preview: resolve stable packages, test, release, then switch channel."""
import base64, hashlib, json, os, re, subprocess, sys, urllib.request, urllib.error
import xml.etree.ElementTree as ET
from pathlib import Path

def stable_version(value):
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:\.\d+)?', value or ''):
        raise ValueError('Preview requires an explicit stable numeric package version: '+str(value))
    parts = tuple(map(int, value.split('.')))
    return parts + (0,) * (4-len(parts))

def choose_version(current, versions):
    stable_version(current)
    candidates = [v for v in versions if re.fullmatch(r'\d+\.\d+\.\d+(?:\.\d+)?', v)]
    return max([current]+candidates, key=stable_version)

def fingerprint(source, packages):
    return hashlib.sha256(json.dumps([source, packages], sort_keys=True).encode()).hexdigest()

def run(*args):
    subprocess.run(args, check=True)

def api(path, data=None, method=None, missing=False):
    req=urllib.request.Request('https://api.github.com/'+path,
        data=None if data is None else json.dumps(data).encode(), method=method,
        headers={'Authorization':'Bearer '+os.environ['GH_TOKEN'], 'User-Agent':'TeamUpdater', 'Accept':'application/vnd.github+json'})
    try:
        with urllib.request.urlopen(req, timeout=60) as response: return json.load(response)
    except urllib.error.HTTPError as error:
        if missing and error.code==404: return None
        raise

def public_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent':'TeamUpdater'}), timeout=60) as r: return json.load(r)

def main():
    config=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    repo=os.environ['GITHUB_REPOSITORY']
    if repo.lower()!=config['projectRepo'].lower(): raise ValueError('Configuration belongs to a different repository')
    root=Path.cwd().resolve(); project=(root/config['project']).resolve()
    if not project.is_relative_to(root): raise ValueError('Project outside repository')
    app=config['appId']; branch='updater-channel'; channel=app+'.json'
    if not re.fullmatch(r'app-[a-z0-9-]+',app): raise ValueError('Invalid app ID')
    public=config['publicRepo']; info=api('repos/'+public)
    if info['private'] or api('repos/'+repo)['private']: raise ValueError('This preview requires public repositories; never embed tokens in clients')
    feed='https://raw.githubusercontent.com/'+public+'/'+info['default_branch']+'/feed/'
    index=public_json(feed+'index.json')
    flat=next(x['@id'] for x in index['resources'] if x['@type'].startswith('PackageBaseAddress/'))
    if not flat.startswith(feed) or not flat.endswith('/'): raise ValueError('Package base must be inside public feed')
    tree=ET.parse(project); selected={}; matched=0
    for ref in tree.getroot().iter('PackageReference'):
        name=ref.get('Include')
        if not name: continue
        if not re.fullmatch(r'[A-Za-z0-9_.-]+',name): raise ValueError('Unsupported package name')
        try: versions=public_json(flat+name.lower()+'/index.json')['versions']
        except urllib.error.HTTPError as error:
            if error.code==404: continue
            raise
        child=ref.find('Version'); current=ref.get('Version') or (child.text if child is not None else None)
        latest=choose_version(current,versions); selected[name]=latest; matched+=1
        if child is not None: child.text=latest
        else: ref.set('Version',latest)
        print(name, current, '->', latest, flush=True)
    if not matched: raise ValueError('No referenced package found in public feed; reference a public package first')
    key=fingerprint(os.environ['GITHUB_SHA'],selected)
    old=api(f'repos/{repo}/contents/{channel}?ref={branch}',missing=True)
    previous=json.loads(base64.b64decode(old['content'])) if old else {}
    if previous.get('buildFingerprint')==key:
        print('No source or dependency changes; nothing published.'); return
    version=f"{config.get('releaseMajor',2)}.{int(os.environ['GITHUB_RUN_NUMBER'])//60000}.{int(os.environ['GITHUB_RUN_NUMBER'])%60000}"
    tree.write(project,encoding='utf-8',xml_declaration=True)
    sources=['--source',feed+'index.json','--source','https://api.nuget.org/v3/index.json']
    if (project.parent/'TeamUpdater.Generated').exists(): sources+=['--source',str(project.parent/'TeamUpdater.Generated')]
    run('dotnet','restore',str(project),*sources)
    version_args=['-p:Version='+version,'-p:AssemblyVersion='+version,'-p:FileVersion='+version]
    run('dotnet','build',str(project),'-c','Release',*version_args)
    for test in config.get('testProjects',[]):
        test_path=(root/test).resolve()
        if not test_path.is_relative_to(root): raise ValueError('Test project outside repository')
        run('dotnet','test',str(test_path),'-c','Release')
    out=root/'_teamupdater'/'publish'; out.mkdir(parents=True,exist_ok=True)
    run('dotnet','publish',str(project),'-c','Release','-r',config.get('runtime','win-x64'),'--self-contained','true',*version_args,'-o',str(out))
    os.environ['TEAM_UPDATER_ASSEMBLY']=str(out/Path(config['entryExe']).with_suffix('.dll'))
    actual=subprocess.check_output(['pwsh','-NoProfile','-Command','[Reflection.AssemblyName]::GetAssemblyName($env:TEAM_UPDATER_ASSEMBLY).Version.ToString(3)'],text=True).strip()
    if actual!=version: raise ValueError('Published assembly version does not match manifest; custom assembly attributes or single-file publishing require manual integration')
    if config.get('selfTestArgument'): run(str(out/config['entryExe']),config['selfTestArgument'])
    # Include resolved dependency versions in the published binary directory and release notes.
    (out/'resolved-dependencies.json').write_text(json.dumps(selected,indent=2),encoding='utf-8')
    import zipfile
    archive=root/'_teamupdater'/f'{app}-{version}.zip'
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        for file in out.rglob('*'):
            if file.is_file(): z.write(file,file.relative_to(out))
    tag=f'{app}-v{version}-a{os.environ.get("GITHUB_RUN_ATTEMPT","1")}'
    manifest={'version':version,'url':f'https://github.com/{repo}/releases/download/{tag}/{archive.name}',
        'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'notes':'Public packages: '+json.dumps(selected),
        'buildFingerprint':key,'packages':selected,'sourceCommit':os.environ['GITHUB_SHA']}
    manifest_file=root/'_teamupdater'/'update.json'; manifest_file.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    run('gh','release','create',tag,str(archive),str(manifest_file),'--repo',repo,'--target',os.environ['GITHUB_SHA'],
        '--title',f'{app} {version}','--notes',manifest['notes'],'--latest=false')
    # Channel moves only after build/tests/archive upload succeeded. Never mutate published ZIPs.
    ref=api(f'repos/{repo}/git/ref/heads/{branch}',missing=True)
    if ref is None: api(f'repos/{repo}/git/refs',{'ref':'refs/heads/'+branch,'sha':os.environ['GITHUB_SHA']})
    payload={'message':f'Publish {app} {version}','branch':branch,'content':base64.b64encode(manifest_file.read_bytes()).decode()}
    if old: payload['sha']=old['sha']
    api(f'repos/{repo}/contents/{channel}',payload,'PUT')
    with open(os.environ['GITHUB_STEP_SUMMARY'],'a',encoding='utf-8') as f:
        f.write(f'Published **{version}**\n\n'+json.dumps(selected)+'\n\n'+manifest['url'])

if __name__=='__main__': main()
