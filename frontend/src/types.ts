export interface Session {
  user_name: string;
  roles: number[];
  team_id?: string;
}

export const ROLE_END_USER = 5;
