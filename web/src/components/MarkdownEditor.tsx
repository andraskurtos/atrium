import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { markdown } from '@codemirror/lang-markdown';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView, keymap, placeholder as cmPlaceholder, type Command } from '@codemirror/view';
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
} from 'react';
import { markdownFormattingKeymap } from '../lib/markdownCommands';
import {
  atriumEditorTheme,
  atriumMarkdownHighlight,
  markdownLivePreviewExtension,
} from '../lib/markdownLivePreview';

export type EditorDisplayMode = 'live' | 'source';

export interface MarkdownEditorHandle {
  runCommand: (command: Command) => void;
  focus: () => void;
}

interface MarkdownEditorProps {
  value: string;
  mode: EditorDisplayMode;
  onChange: (nextValue: string) => void;
}

export const MarkdownEditor = forwardRef<
  MarkdownEditorHandle,
  MarkdownEditorProps
>(function MarkdownEditor({ value, mode, onChange }, ref) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const viewRef = useRef<EditorView | null>(null);
  const liveCompartmentRef = useRef(new Compartment());
  const onChangeRef = useRef(onChange);
  const initialValueRef = useRef(value);
  const initialModeRef = useRef(mode);

  onChangeRef.current = onChange;

  useImperativeHandle(
    ref,
    () => ({
      runCommand(command: Command) {
        const view = viewRef.current;
        if (!view) return;
        command(view);
      },
      focus() {
        viewRef.current?.focus();
      },
    }),
    [],
  );

  // Mount CodeMirror once and preserve undo history / cursor across Live Preview <-> Source toggles
  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;

    const liveCompartment = liveCompartmentRef.current;

    const state = EditorState.create({
      doc: initialValueRef.current,
      extensions: [
        history(),
        markdownFormattingKeymap,
        keymap.of([...defaultKeymap, ...historyKeymap]),
        markdown(),
        EditorView.lineWrapping,
        atriumEditorTheme,
        atriumMarkdownHighlight,
        cmPlaceholder('Start writing in Markdown…'),
        liveCompartment.of(
          initialModeRef.current === 'live' ? markdownLivePreviewExtension : [],
        ),
        EditorView.updateListener.of((update) => {
          if (update.docChanged) {
            onChangeRef.current(update.state.doc.toString());
          }
        }),
      ],
    });

    const view = new EditorView({ state, parent: host });
    viewRef.current = view;

    return () => {
      view.destroy();
      viewRef.current = null;
    };
  }, []);

  // Reconfigure Live Preview compartment dynamically on mode switch
  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    view.dispatch({
      effects: liveCompartmentRef.current.reconfigure(
        mode === 'live' ? markdownLivePreviewExtension : [],
      ),
    });
  }, [mode]);

  // Sync external document replacements (e.g. Reset sample) without clobbering user typing
  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    const current = view.state.doc.toString();
    if (value !== current) {
      view.dispatch({
        changes: { from: 0, to: current.length, insert: value },
      });
    }
  }, [value]);

  return (
    <div
      ref={hostRef}
      className={`notes-cm-host is-${mode}`}
      aria-label="Markdown editor"
    />
  );
});

