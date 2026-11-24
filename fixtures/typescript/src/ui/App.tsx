import { format } from '@state';

export interface Props {
    count: number;
}

export function App({ count }: Props) {
    return <main>
        {count > 0 && <strong>{format(count)}</strong>}
        {count > 1 && <span>One</span>}
        {count > 2 && <span>Two</span>}
        {count > 3 && <span>Three</span>}
        {count > 4 && <span>Four</span>}
        {count > 5 && <span>Five</span>}
        {count > 6 && <span>Six</span>}
        {count > 7 && <span>Seven</span>}
        {[count].map(value => <small>{value}</small>)}
    </main>;
}
