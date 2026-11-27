import { EditorSelection, type Extension } from '@codemirror/state';
import { keymap, type Command } from '@codemirror/view';

function countStarRun(
  text: string,
  pos: number,
  dir: 'forward' | 'backward',
): number {
  let count = 0;
  if (dir === 'backward') {
    for (let i = pos - 1; i >= 0 && text[i] === '*'; i -= 1) {
      count += 1;
    }
  } else {
    for (let i = pos; i < text.length && text[i] === '*'; i += 1) {
      count += 1;
    }
  }
  return count;
}

function hasStandaloneItalicMarkers(
  text: string,
  pos: number,
  side: 'before' | 'after',
): boolean {
  // Odd number of '*' (1 or 3) indicates italic is present; 2 means bold only
  const count = countStarRun(
    text,
    pos,
    side === 'before' ? 'backward' : 'forward',
  );
  return count % 2 === 1;
}

function hasInternalClosingMarker(interior: string, marker: string): boolean {
  if (marker === '**') {
    return interior.includes('**');
  }
  if (marker === '*') {
    for (let i = 0; i < interior.length; ) {
      if (interior[i] === '*') {
        const run = countStarRun(interior, i, 'forward');
        if (run % 2 === 1) return true;
        i += run;
      } else {
        i += 1;
      }
    }
    return false;
  }
  return interior.includes(marker);
}

function isSingleWrappedSelection(selected: string, marker: string): boolean {
  const mLen = marker.length;
  if (
    selected.length < mLen * 2 ||
    !selected.startsWith(marker) ||
    !selected.endsWith(marker)
  ) {
    return false;
  }

  if (marker === '*') {
    const openRun = countStarRun(selected, 0, 'forward');
    if (openRun === selected.length) {
      return selected.length === 2;
    }
    const closeRun = countStarRun(selected, selected.length, 'backward');
    if (openRun % 2 !== 1 || closeRun % 2 !== 1) {
      return false;
    }
    const interior = selected.slice(openRun, selected.length - closeRun);
    return !hasInternalClosingMarker(interior, '*');
  }

  const interior = selected.slice(mLen, -mLen);
  return !hasInternalClosingMarker(interior, marker);
}

function toggleInlineWrapper(marker: string): Command {
  return (view) => {
    const { state } = view;
    const mLen = marker.length;
    const docText = state.doc.toString();
    const changes = state.changeByRange((range) => {
      const selected = state.sliceDoc(range.from, range.to);

      // Unwrap if surrounding text already has the marker (avoiding stripping '*' from '**' bold)
      const before =
        range.from >= mLen
          ? state.sliceDoc(range.from - mLen, range.from)
          : '';
      const after =
        range.to + mLen <= state.doc.length
          ? state.sliceDoc(range.to, range.to + mLen)
          : '';
      const surroundingMatches =
        !hasInternalClosingMarker(selected, marker) &&
        (marker === '*'
          ? before === '*' &&
            after === '*' &&
            hasStandaloneItalicMarkers(docText, range.from, 'before') &&
            hasStandaloneItalicMarkers(docText, range.to, 'after')
          : before === marker && after === marker);

      if (surroundingMatches) {
        return {
          changes: [
            { from: range.from - mLen, to: range.from, insert: '' },
            { from: range.to, to: range.to + mLen, insert: '' },
          ],
          range: EditorSelection.range(range.from - mLen, range.to - mLen),
        };
      }

      // Unwrap if the selection itself is a single wrapped span (not two separate spans like "**foo** and **bar**")
      if (isSingleWrappedSelection(selected, marker)) {
        const inner = selected.slice(mLen, -mLen);
        return {
          changes: { from: range.from, to: range.to, insert: inner },
          range: EditorSelection.range(range.from, range.from + inner.length),
        };
      }

      const wrapped = `${marker}${selected}${marker}`;
      return {
        changes: { from: range.from, to: range.to, insert: wrapped },
        range: EditorSelection.range(
          range.from + mLen,
          range.from + mLen + selected.length,
        ),
      };
    });

    view.dispatch(changes);
    view.focus();
    return true;
  };
}

export const toggleBold: Command = toggleInlineWrapper('**');

export const toggleItalic: Command = toggleInlineWrapper('*');

export function setHeading(level: 1 | 2 | 3): Command {
  return (view) => {
    const { state } = view;
    const prefix = `${'#'.repeat(level)} `;
    const line = state.doc.lineAt(state.selection.main.head);
    const match = line.text.match(/^(#{1,6})(?:\s+|$)/);

    const from = line.from;
    let to = line.from;
    let insert = prefix;

    if (match) {
      to = line.from + match[0].length;
      insert = match[1].length === level ? '' : prefix;
    }

    const changeSpec = { from, to, insert };
    const nextCursor = state
      .changes(changeSpec)
      .mapPos(state.selection.main.head, 1);

    view.dispatch({
      changes: changeSpec,
      selection: EditorSelection.cursor(nextCursor),
    });
    view.focus();
    return true;
  };
}

export const toggleBlockquote: Command = (view) => {
  const { state } = view;
  const trSpec = state.changeByRange((range) => {
    const lines = [];
    for (let pos = range.from; pos <= range.to; ) {
      const line = state.doc.lineAt(pos);
      if (range.empty || range.to > line.from) {
        lines.push(line);
      }
      pos = line.to + 1;
    }

    const allQuoted =
      lines.length > 0 && lines.every((line) => /^> ?/.test(line.text));

    const changes: Array<{ from: number; to: number; insert: string }> = [];
    for (const line of lines) {
      const match = line.text.match(/^> ?/);
      if (allQuoted && match) {
        changes.push({
          from: line.from,
          to: line.from + match[0].length,
          insert: '',
        });
      } else if (!allQuoted && !match) {
        changes.push({ from: line.from, to: line.from, insert: '> ' });
      }
    }

    const changeSet = state.changes(changes);
    const nextFrom = changeSet.mapPos(range.from, 1);
    const nextTo = range.empty
      ? nextFrom
      : Math.max(nextFrom, changeSet.mapPos(range.to, -1));
    const nextRange =
      range.anchor <= range.head
        ? EditorSelection.range(nextFrom, nextTo)
        : EditorSelection.range(nextTo, nextFrom);

    return {
      changes,
      range: nextRange,
    };
  });

  view.dispatch(trSpec);
  view.focus();
  return true;
};

export const insertLink: Command = (view) => {
  const { state } = view;
  const range = state.selection.main;
  const selected = state.sliceDoc(range.from, range.to);
  const label = selected || 'link text';
  const urlPlaceholder = 'https://';
  const insert = `[${label}](${urlPlaceholder})`;
  const urlStart = range.from + 1 + label.length + 2;

  view.dispatch({
    changes: { from: range.from, to: range.to, insert },
    selection: EditorSelection.range(urlStart, urlStart + urlPlaceholder.length),
  });
  view.focus();
  return true;
};

export const insertImage: Command = (view) => {
  const { state } = view;
  const range = state.selection.main;
  const selected = state.sliceDoc(range.from, range.to);
  const alt = selected || 'image description';
  const urlPlaceholder = 'https://';
  const insert = `![${alt}](${urlPlaceholder})`;
  const urlStart = range.from + 2 + alt.length + 2;

  view.dispatch({
    changes: { from: range.from, to: range.to, insert },
    selection: EditorSelection.range(urlStart, urlStart + urlPlaceholder.length),
  });
  view.focus();
  return true;
};

export const insertHorizontalRule: Command = (view) => {
  const { state } = view;
  const pos = state.selection.main.head;
  const line = state.doc.lineAt(pos);
  const needsLeadingNewline = line.text.trim().length > 0;
  const insert = needsLeadingNewline ? '\n\n---\n\n' : '---\n\n';
  const insertFrom = needsLeadingNewline ? line.to : line.from;
  const insertTo = line.to;

  view.dispatch({
    changes: { from: insertFrom, to: insertTo, insert },
    selection: EditorSelection.cursor(insertFrom + insert.length),
  });
  view.focus();
  return true;
};

export const insertCallout: Command = (view) => {
  const { state } = view;
  const range = state.selection.main;
  const startLine = state.doc.lineAt(range.from);
  const header = '> [!note] Note\n';

  if (range.empty) {
    const needsLeadingNewline = startLine.text.trim().length > 0;
    const prefix = needsLeadingNewline ? '\n\n' : '';
    const insertFrom = needsLeadingNewline ? startLine.to : startLine.from;
    const insertTo = startLine.to;
    const afterLine = state.doc.sliceString(insertTo, insertTo + 2);
    const suffix =
      insertTo < state.doc.length && afterLine !== '\n\n' ? '\n' : '';
    const insert = `${prefix}${header}> ${suffix}`;
    const titleStart = insertFrom + prefix.length + '> [!note] '.length;

    view.dispatch({
      changes: { from: insertFrom, to: insertTo, insert },
      selection: EditorSelection.range(
        titleStart,
        titleStart + 'Note'.length,
      ),
    });
    view.focus();
    return true;
  }

  const rawSelected = state.sliceDoc(range.from, range.to);
  const hasTrailingNewline = rawSelected.endsWith('\n');
  const trimmedSelection = hasTrailingNewline
    ? rawSelected.slice(0, -1)
    : rawSelected;
  const body = trimmedSelection
    .split('\n')
    .map((line) => `> ${line}`)
    .join('\n');

  const textBefore = state.sliceDoc(startLine.from, range.from);
  const hasTextBefore = textBefore.trim().length > 0;
  const insertFrom = hasTextBefore
    ? startLine.from + textBefore.trimEnd().length
    : startLine.from;
  const prefix = hasTextBefore ? '\n\n' : '';

  const selectionEnd = hasTrailingNewline ? range.to - 1 : range.to;
  const endLine = state.doc.lineAt(selectionEnd);
  const textAfter = state.sliceDoc(selectionEnd, endLine.to);
  const hasTextAfter = textAfter.trim().length > 0;
  const insertTo = hasTextAfter
    ? selectionEnd + (textAfter.length - textAfter.trimStart().length)
    : endLine.to;

  const afterInsert = state.doc.sliceString(insertTo, insertTo + 2);
  const suffix = hasTextAfter
    ? '\n\n'
    : insertTo < state.doc.length && afterInsert !== '\n\n'
      ? '\n'
      : '';

  const insert = `${prefix}${header}${body}${suffix}`;
  const titleStart = insertFrom + prefix.length + '> [!note] '.length;

  view.dispatch({
    changes: { from: insertFrom, to: insertTo, insert },
    selection: EditorSelection.range(titleStart, titleStart + 'Note'.length),
  });
  view.focus();
  return true;
};

export const markdownFormattingKeymap: Extension = keymap.of([
  { key: 'Mod-b', run: toggleBold },
  { key: 'Mod-i', run: toggleItalic },
  { key: 'Mod-Shift-k', run: insertLink, stopPropagation: true },
]);

