import test from 'node:test';
import assert from 'node:assert/strict';
import packageJson from '../package.json';

type CommandContribution = {
    command?: string;
};

type MenuContribution = {
    command?: string;
    when?: string;
};

test('F-003: spec tree title bar hides the reload action while keeping the command contribution', () => {
    const commands = (packageJson.contributes?.commands ?? []) as CommandContribution[];
    const titleMenu = ((packageJson.contributes?.menus as { 'view/title'?: MenuContribution[] } | undefined)?.['view/title'] ?? []);

    assert.ok(
        commands.some((contribution) => contribution.command === 'flowExt.reloadWindow'),
        'reload command should remain contributed for command palette and automation',
    );
    assert.ok(
        !titleMenu.some((contribution) =>
            contribution.command === 'flowExt.reloadWindow' && contribution.when === 'view == specTree'
        ),
        'reload command should not be exposed in the spec tree title bar',
    );
});
