import type {ChatEvent, Citation} from './chatStream';

export type AnswerState = {
  text: string;
  citations: Citation[];
  phase: 'idle' | 'generating' | 'confirmed' | 'limited' | 'failed' | 'timeout' | 'cancelled';
};
export const initialAnswer: AnswerState = {text: '', citations: [], phase: 'idle'};
type Action = ChatEvent | {type: 'begin'} | {type: 'failed' | 'timeout' | 'cancelled'};

// The same state transition withdraws provisional text on every unsuccessful path.
export function answerReducer(state: AnswerState, action: Action): AnswerState {
  switch (action.type) {
    case 'begin': return {...initialAnswer, phase: 'generating'};
    case 'delta': return {...state, text: state.text + action.text, phase: 'generating'};
    case 'done': return action.limited
      ? {...initialAnswer, phase: 'limited'}
      : {...state, citations: action.citations, phase: 'confirmed'};
    case 'error': return {...initialAnswer, phase: action.code === 'timeout' ? 'timeout' : 'failed'};
    case 'failed':
    case 'timeout':
    case 'cancelled': return {...initialAnswer, phase: action.type};
    default: return state;
  }
}
