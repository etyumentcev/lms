import { array, count, object, string } from './validation';
import type { MarkdownAsset } from '../../components/MarkdownContent';

export type TeacherReferenceMaterial = { id:string;kind:'solution'|'rubric'|'notes';title:string|null;order:number;
  targetKind:'course'|'lesson'|'problem';targetId:string;bodyMarkdown:string };
export type TeacherReference = { contentVersionId:string;versionNumber:number;courseId:string;lessonId:string;problemId:string;
  materials:TeacherReferenceMaterial[];assets:(MarkdownAsset&{id:string;ownerKind:string;ownerId:string})[] };
const nullableString=(v:unknown)=>v===null||string(v);
const material=object({id:string,kind:(v:unknown)=>['solution','rubric','notes'].includes(String(v)),title:nullableString,order:count,
  targetKind:(v:unknown)=>['course','lesson','problem'].includes(String(v)),targetId:string,bodyMarkdown:string});
const asset=object({id:string,ownerKind:(v:unknown)=>['course','lesson','problem'].includes(String(v)),ownerId:string,mediaType:string,artifactPath:string});
const reference=object({contentVersionId:string,versionNumber:count,courseId:string,lessonId:string,problemId:string,
  materials:array(material),assets:array(asset)});
export function decodeTeacherReference(value:unknown):TeacherReference{
  if(!reference(value))throw new Error('Invalid teacher reference');
  const data=value as TeacherReference;if(data.assets.some(x=>!/^assets\/[a-z][a-z0-9-]*\.(png|jpg|jpeg|webp|txt)$/i.test(x.artifactPath)))throw new Error('Invalid teacher asset');
  return data;
}
