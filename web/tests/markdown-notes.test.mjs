import test from 'node:test';
import assert from 'node:assert/strict';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ReactMarkdown from 'react-markdown';
import { markdown } from '@codemirror/lang-markdown';
import { EditorSelection, EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import {
  CALLOUT_GLYPHS,
  clearNotesDraft,
  DEFAULT_SAMPLE_NOTE,
  getNotesDraftStorageKey,
  loadNotesDraft,
  NOTES_DRAFT_STORAGE_KEY,
  parseCalloutHeaderLine,
  resolveCalloutTone,
  sanitizeMarkdownUrl,
  saveNotesDraft,
} from '../src/lib/markdownUtils.ts';
import { readingMarkdownComponents } from '../src/lib/markdownReading.ts';
import {
  insertCallout,
  setHeading,
  toggleBlockquote,
  toggleBold,
  toggleItalic,
} from '../src/lib/markdownCommands.ts';
import { markdownLivePreviewExtension } from '../src/lib/markdownLivePreview.ts';

function renderReadingHtml(md) {
  return renderToStaticMarkup(
    createElement(ReactMarkdown, { components: readingMarkdownComponents }, md),
  );
}

function runEditorCommand(doc, from, to, command) {
  let state = EditorState.create({
    doc,
    selection: EditorSelection.range(from, to),
  });
  const fakeView = {
    get state() {
      return state;
    },
    dispatch(spec) {
      state = state.update(spec).state;
    },
    focus() {},
  };
  command(fakeView);
  return {
    doc: state.doc.toString(),
    from: state.selection.main.from,
    to: state.selection.main.to,
    selected: state.sliceDoc(
      state.selection.main.from,
      state.selection.main.to,
    ),
  };
}

function collectLivePreviewDecorations(doc, cursorPos = doc.length) {
  const state = EditorState.create({
    doc,
    selection: EditorSelection.cursor(cursorPos),
    extensions: [markdown(), markdownLivePreviewExtension],
  });
  const providers = state.facet(EditorView.decorations);
  const items = [];
  for (const provider of providers) {
    const decoSet = typeof provider === 'function' ? provider( null ) : provider;
    const iter = decoSet.iter();
    while (iter.value) {
      items.push({
        from: iter.from,
        to: iter.to,
        value: iter.value,
        spec: iter.value.spec,
      });
      iter.next();
    }
  }
  return { state, items };
}

test('resolveCalloutTone safely handles Object.prototype keys', () => {
  for (const protoKey of [
    'constructor',
    '__proto__',
    'toString',
    'valueOf',
    'hasOwnProperty',
  ]) {
    const tone = resolveCalloutTone(protoKey);
    assert.equal(tone, 'note');
    assert.equal(CALLOUT_GLYPHS[tone], 'ℹ');
  }

  assert.equal(resolveCalloutTone('TIP'), 'tip');
  assert.equal(resolveCalloutTone('warning'), 'warning');
  assert.equal(resolveCalloutTone('bug'), 'danger');
  assert.equal(resolveCalloutTone('example'), 'quote');

  const parsed = parseCalloutHeaderLine('> [!constructor] Custom');
  assert.deepEqual(parsed, {
    rawType: 'constructor',
    tone: 'note',
    title: 'Custom',
  });
});

test('Reading mode callouts preserve inline-formatted titles and strip hard breaks', () => {
  const formatted = renderReadingHtml('> [!tip] Use **bold** title\n> Body');
  assert.match(
    formatted,
    /<div class="notes-callout is-tip">.*<span>Use <strong>bold<\/strong> title<\/span>.*<div class="notes-callout-body"><p>Body<\/p><\/div><\/div>/,
  );

  const leadingBold = renderReadingHtml('> [!note] **Important**\n> Body');
  assert.match(leadingBold, /<span><strong>Important<\/strong><\/span>/);
  assert.doesNotMatch(leadingBold, /<span>Note<\/span>/);
  assert.match(
    leadingBold,
    /<div class="notes-callout-body"><p>Body<\/p><\/div>/,
  );

  const hardBreak = renderReadingHtml('> [!note] Title  \n> Body');
  assert.match(hardBreak, /<span>Title<\/span>/);
  assert.match(
    hardBreak,
    /<div class="notes-callout-body"><p>Body<\/p><\/div>/,
  );
  assert.doesNotMatch(hardBreak, /<br\s*\/?>/);
});

test('Reading mode ignores non-paragraph blockquotes and invalid callout boundaries', () => {
  const headingQuote = renderReadingHtml('> # [!note] Heading');
  assert.match(
    headingQuote,
    /^<blockquote>\s*<h1>\[!note\] Heading<\/h1>\s*<\/blockquote>$/,
  );

  const noSpaceAdjacent = renderReadingHtml('> [!note]**bold**');
  assert.match(
    noSpaceAdjacent,
    /^<blockquote>\s*<p>\[!note\]<strong>bold<\/strong><\/p>\s*<\/blockquote>$/,
  );

  const protoCallout = renderReadingHtml('> [!constructor]\n> Safe');
  assert.match(protoCallout, /class="notes-callout is-note"/);
  assert.match(protoCallout, /ℹ/);
  assert.match(protoCallout, /<span>Constructor<\/span>/);

  const liveProto = collectLivePreviewDecorations('> [!__proto__]\n> Safe\n\nEnd');
  const protoWidgets = liveProto.items.filter(
    (d) =>
      d.spec.widget && d.spec.widget.constructor.name === 'CalloutHeaderWidget',
  );
  assert.equal(protoWidgets.length, 1);
  assert.equal(protoWidgets[0].spec.widget.tone, 'note');
});

test('Reading mode unwraps standalone images into <figure> and keeps inline images phrasing-safe', () => {
  const standalone = renderReadingHtml(
    '![Warm sunlit desk notebook](https://example.com/notebook.jpg)',
  );
  assert.doesNotMatch(standalone, /<p><figure/);
  assert.match(standalone, /^<figure class="cm-md-image-figure">/);
  assert.match(standalone, /referrerPolicy="no-referrer"/i);
  assert.match(
    standalone,
    /<figcaption class="cm-md-image-caption">Warm sunlit desk notebook<\/figcaption><\/figure>$/,
  );

  const inlineInParagraph = renderReadingHtml(
    'See ![icon](https://example.com/icon.png) inline.',
  );
  assert.doesNotMatch(inlineInParagraph, /<figure/);
  assert.match(
    inlineInParagraph,
    /^<p>See <span class="notes-reading-inline-image"><img [^>]*referrerPolicy="no-referrer"[^>]*\/><\/span> inline\.<\/p>$/i,
  );

  const blocked = renderReadingHtml('![xss](javascript:alert(1))');
  assert.doesNotMatch(blocked, /<img/);
  assert.match(blocked, /Blocked or invalid image URL \(xss\)/);

  const sampleHtml = renderReadingHtml(DEFAULT_SAMPLE_NOTE);
  assert.doesNotMatch(sampleHtml, /<p><figure/);
  assert.match(sampleHtml, /<code>Cmd\/Ctrl \+ E<\/code>/);
});

test('Live Preview avoids overlapping replace decorations on multi-line callout children', () => {
  const doc =
    '> [!note] ![alt\n> text](https://example.com/a.png)\n\nOutside line';
  const { items } = collectLivePreviewDecorations(doc, doc.length);

  // Filter replace decorations (non-mark, non-line, non-point-widget-at-pos)
  const replaceRanges = items.filter(
    (d) =>
      d.from < d.to &&
      !d.spec.class &&
      !d.spec.attributes,
  );
  assert.ok(replaceRanges.length >= 2);
  for (let i = 1; i < replaceRanges.length; i += 1) {
    assert.ok(
      replaceRanges[i - 1].to <= replaceRanges[i].from,
      `Expected non-overlapping replace ranges, got [${replaceRanges[i - 1].from}, ${replaceRanges[i - 1].to}] and [${replaceRanges[i].from}, ${replaceRanges[i].to}]`,
    );
  }
});

test('Live Preview handles reference-style images, nested callouts, and HorizontalRuleWidget.eq', () => {
  const refImgDoc = '![alt][1]\n\n[1]: https://example.com/a.png\n\nCursor line';
  const { items: refItems } = collectLivePreviewDecorations(refImgDoc);
  const imageWidgets = refItems.filter(
    (d) => d.spec.widget && d.spec.widget.constructor.name === 'ImageWidget',
  );
  assert.equal(imageWidgets.length, 0);

  const nestedDoc =
    '- > [!tip] List callout\n  > Body\n\n> > [!warning] Nested\n> > Text\n\nEnd';
  const { items: nestedItems } = collectLivePreviewDecorations(nestedDoc);
  const calloutWidgets = nestedItems.filter(
    (d) =>
      d.spec.widget && d.spec.widget.constructor.name === 'CalloutHeaderWidget',
  );
  assert.equal(calloutWidgets.length, 2);
  assert.equal(calloutWidgets[0].spec.widget.tone, 'tip');
  assert.equal(calloutWidgets[0].spec.widget.title, 'List callout');
  assert.equal(calloutWidgets[1].spec.widget.tone, 'warning');
  assert.equal(calloutWidgets[1].spec.widget.title, 'Nested');

  const hrDoc = '---\n\n---\n\nEnd';
  const { items: hrItems } = collectLivePreviewDecorations(hrDoc);
  const hrWidgets = hrItems
    .filter(
      (d) =>
        d.spec.widget &&
        d.spec.widget.constructor.name === 'HorizontalRuleWidget',
    )
    .map((d) => d.spec.widget);
  assert.equal(hrWidgets.length, 2);
  assert.equal(hrWidgets[0].eq(hrWidgets[1]), true);
});

test('Formatting commands handle multi-span inline selection, bare-# headings, bare-> blockquotes, and mid-line callouts', () => {
  // 1. toggleBold & toggleItalic across two formatted spans
  const twoBolds = '**foo** and **bar**';
  const boldRes = runEditorCommand(twoBolds, 0, twoBolds.length, toggleBold);
  assert.notEqual(boldRes.doc, 'foo** and **bar');

  const singleBoldWithInnerItalic = '**foo *bar* baz**';
  assert.equal(
    runEditorCommand(
      singleBoldWithInnerItalic,
      0,
      singleBoldWithInnerItalic.length,
      toggleBold,
    ).doc,
    'foo *bar* baz',
  );

  const twoItalics = '*foo* and *bar*';
  const italicRes = runEditorCommand(
    twoItalics,
    0,
    twoItalics.length,
    toggleItalic,
  );
  assert.notEqual(italicRes.doc, 'foo* and *bar');

  const singleItalicWithInnerBold = '*foo **bar** baz*';
  assert.equal(
    runEditorCommand(
      singleItalicWithInnerBold,
      0,
      singleItalicWithInnerBold.length,
      toggleItalic,
    ).doc,
    'foo **bar** baz',
  );

  // 2. setHeading on bare '#' line
  assert.equal(runEditorCommand('#', 1, 1, setHeading(1)).doc, '');
  assert.equal(runEditorCommand('#', 1, 1, setHeading(2)).doc, '## ');

  // 3. toggleBlockquote with bare '>' blank line & Shift+Down col-0 selection
  const multiParaQuote = '> p1\n>\n> p2';
  assert.equal(
    runEditorCommand(multiParaQuote, 0, multiParaQuote.length, toggleBlockquote)
      .doc,
    'p1\n\np2',
  );

  const twoLines = 'Hello\nWorld';
  const shiftDownQuoted = runEditorCommand(twoLines, 0, 6, toggleBlockquote);
  assert.equal(shiftDownQuoted.doc, '> Hello\nWorld');
  assert.equal(shiftDownQuoted.to, 8);

  // 4. insertCallout on mid-line selection
  const midLine = 'Intro selected outro';
  const calloutRes = runEditorCommand(midLine, 6, 14, insertCallout);
  assert.equal(
    calloutRes.doc,
    'Intro\n\n> [!note] Note\n> selected\n\noutro',
  );
  assert.equal(calloutRes.selected, 'Note');
});

test('User-scoped localStorage draft helpers isolate accounts and clean up on sign-out', () => {
  const store = new Map();
  const prevWindow = globalThis.window;
  globalThis.window = {
    localStorage: {
      getItem: (k) => (store.has(k) ? store.get(k) : null),
      setItem: (k, v) => store.set(k, String(v)),
      removeItem: (k) => store.delete(k),
    },
  };

  try {
    assert.equal(
      getNotesDraftStorageKey('user-1'),
      `${NOTES_DRAFT_STORAGE_KEY}:user-1`,
    );
    assert.equal(loadNotesDraft('user-1'), DEFAULT_SAMPLE_NOTE);
    assert.equal(store.size, 0);

    saveNotesDraft('Alice private note', 'user-1');
    saveNotesDraft('Bob private note', 'user-2');
    store.set(NOTES_DRAFT_STORAGE_KEY, 'Legacy draft');

    assert.equal(loadNotesDraft('user-1'), 'Alice private note');
    assert.equal(loadNotesDraft('user-2'), 'Bob private note');

    // Saving DEFAULT_SAMPLE_NOTE removes redundant storage item
    saveNotesDraft(DEFAULT_SAMPLE_NOTE, 'user-2');
    assert.equal(store.has(getNotesDraftStorageKey('user-2')), false);

    // Sign-out cleanup removes user-1 draft + legacy unscoped key without touching other users
    saveNotesDraft('Bob note 2', 'user-2');
    clearNotesDraft('user-1');
    assert.equal(store.has(getNotesDraftStorageKey('user-1')), false);
    assert.equal(store.has(NOTES_DRAFT_STORAGE_KEY), false);
    assert.equal(loadNotesDraft('user-2'), 'Bob note 2');
  } finally {
    globalThis.window = prevWindow;
  }

  assert.equal(sanitizeMarkdownUrl('https://example.com/a', 'link'), 'https://example.com/a');
  assert.equal(sanitizeMarkdownUrl('mailto:hi@example.com', 'link'), 'mailto:hi@example.com');
  assert.equal(sanitizeMarkdownUrl('mailto:hi@example.com', 'image'), null);
  assert.equal(sanitizeMarkdownUrl('javascript:alert(1)', 'link'), null);
});
