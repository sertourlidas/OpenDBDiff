using System;
using System.Collections.Generic;
using System.Text;
using DBDiff.Schema.Model;
using DBDiff.Schema.Attributes;

namespace DBDiff.Schema.SQLServer.Model
{
    public class Table : SQLServerSchemaBase, IComparable<Table>
    {
        private Columns columns;
        private Constraints constraints;
        private TableOptions options;
        private Triggers triggers;
        private Table originalTable;
        private Indexes indexes;
        private Boolean hasClusteredIndex;
        private int dependenciesCount;
        private string fileGroup;
        private string fileGroupText;
        private List<ISchemaBase> dependencis = null;

        public Table(Database parent):base(Enums.ObjectType.Table)
        {
            this.Parent = parent;
            dependenciesCount = -1;
            columns = new Columns(this);
            constraints = new Constraints(this);
            options = new TableOptions(this);
            triggers = new Triggers(this);
            indexes = new Indexes(this);            
        }

        /// <summary>
        /// Clona el objeto Table en una nueva instancia.
        /// </summary>
        public Table Clone(Database objectParent)
        {
            Table table = new Table(objectParent);
            table.Owner = this.Owner;
            table.Name = this.Name;
            table.Id = this.Id;
            table.Guid = this.Guid;
            table.Status = this.Status;
            table.FileGroup = this.FileGroup;
            table.FileGroupText = this.FileGroupText;
            table.HasClusteredIndex = this.HasClusteredIndex;
            table.dependenciesCount = this.DependenciesCount;
            table.Columns = this.Columns.Clone(table);
            table.Options = this.Options.Clone(table);
            table.triggers = this.Triggers.Clone(table);
            table.indexes = this.Indexes.Clone(table);
            return table;
        }

        public TableOptions Options
        {
            get { return options; }
            set { options = value; }
        }

        public string FileGroupText
        {
            get { return fileGroupText; }
            set { fileGroupText = value; }
        }

        /// <summary>
        /// Indica si la tabla tiene algun indice Clustered (de lo contrario, es una tabla Heap)
        /// </summary>
        public Boolean HasClusteredIndex
        {
            get { return hasClusteredIndex; }
            set { hasClusteredIndex = value; }
        }

        /// <summary>
        /// Indica si la tabla tiene alguna columna que sea Identity.
        /// </summary>
        public Boolean HasIdentityColumn
        {
            get 
            {
                foreach (Column col in Columns)
                { 
                    if (col.IsIdentity) return true; 
                }
                return false;
            }
        }

        public Boolean HasBlobColumn
        {
            get
            {
                foreach (Column col in Columns)
                {
                    if (col.IsBLOB) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Objeto tabla con la definicion original de la tabla. Se usa cuando se requiere regenerar la tabla.
        /// </summary>
        public Table OriginalTable
        {
            get { return originalTable; }
            set { originalTable = value; }
        }

        /// <summary>
        /// Colecion de constraints de la tabla.
        /// </summary>
        [ShowItemAttribute("Constraints")]
        public Constraints Constraints
        {
            get { return constraints; }
        }

        /// <summary>
        /// Coleccion de campos de la tabla.
        /// </summary>
        [ShowItemAttribute("Columns","Column")]
        public Columns Columns
        {
            get { return columns; }
            set { columns = value; }
        }

        [ShowItemAttribute("Indexes","Index")]
        public Indexes Indexes
        {
            get { return indexes; }
        }

        [ShowItemAttribute("Triggers")]
        public Triggers Triggers
        {
            get { return triggers; }
        }

        /// <summary>
        /// Filegroup donde se encuentra la tabla.
        /// </summary>
        public string FileGroup
        {
            get { return fileGroup; }
            set { fileGroup = value; }
        }

        public override string ToSql()
        {
            return ToSQL(true);
        }

        /// <summary>
        /// Devuelve el schema de la tabla en formato SQL.
        /// </summary>
        public string ToSQL(Boolean showFK)
        {
            string sql = "";
            if (columns.Count > 0)
            {
                sql += "CREATE TABLE " + FullName + "\r\n(\r\n";
                sql += columns.ToSQL();
                if (constraints.Count > 0)
                {
                    sql += ",\r\n";
                    sql += constraints.ToSQL(Constraint.ConstraintType.PrimaryKey);
                    sql += constraints.ToSQL(Constraint.ConstraintType.Unique);
                    if (showFK)
                        sql += constraints.ToSQL(Constraint.ConstraintType.ForeignKey);
                }
                else
                    sql += "\r\n";
                sql += ")";
                if (!String.IsNullOrEmpty(FileGroup)) sql += " ON [" + FileGroup + "]";
                if (!String.IsNullOrEmpty(FileGroupText))
                {
                    if (this.HasBlobColumn)
                        sql += " TEXTIMAGE_ON [" + FileGroupText + "]";
                }
                sql += "\r\n";
                sql += "GO\r\n";
                sql += constraints.ToSQLAdd(Constraint.ConstraintType.Check);
                sql += indexes.ToSQL();
                sql += options.ToSQL();
                sql += triggers.ToSQL();
            }
            return sql;

        }

        public override string ToSqlAdd()
        {
            return ToSql();
        }

        public override string ToSqlDrop()
        {
            return "DROP TABLE " + FullName + "\r\nGO\r\n";
        }

        private SQLScriptList BuildSQLFileGroup()
        {
            SQLScriptList listDiff = new SQLScriptList();

            Boolean found = false;
            Index clustered = indexes.Find(Index.IndexTypeEnum.Clustered);
            if (clustered == null)
            {
                foreach (Constraint cons in constraints)
                {
                    if (cons.Index.Type == Index.IndexTypeEnum.Clustered)
                    {
                        listDiff.Add(cons.ToSQLDrop(FileGroup), dependenciesCount, Enums.ScripActionType.DropConstraint);
                        listDiff.Add(cons.ToSqlAdd(), dependenciesCount, Enums.ScripActionType.AddConstraint);
                        found = true;
                    }
                }
                if (!found)
                {
                    this.Status = Enums.ObjectStatusType.AlterRebuildStatus;
                    listDiff = ToSQLDiff();
                }
            }
            else
            {
                listDiff.Add(clustered.ToSqlDrop(FileGroup), dependenciesCount, Enums.ScripActionType.DropIndex);
                listDiff.Add(clustered.ToSqlAdd(), dependenciesCount, Enums.ScripActionType.AddIndex);
            }
            return listDiff;
        }

        /// <summary>
        /// Devuelve el schema de diferencias de la tabla en formato SQL.
        /// </summary>
        public SQLScriptList ToSQLDiff()
        {
            SQLScriptList listDiff = new SQLScriptList();

            if (this.Status == Enums.ObjectStatusType.DropStatus)
            {
                if (((Database)Parent).Options.Ignore.FilterTable)
                {
                    listDiff.Add(ToSqlDrop(), dependenciesCount, Enums.ScripActionType.DropTable);
                    listDiff.AddRange(ToSQLDropFKBelow());
                }
            }
            if (this.Status == Enums.ObjectStatusType.CreateStatus)
            {
                listDiff.Add(ToSQL(false), dependenciesCount, Enums.ScripActionType.AddTable);
                listDiff.Add(Constraints.ToSQLAdd(Constraint.ConstraintType.ForeignKey), dependenciesCount, Enums.ScripActionType.AddConstraintFK);
            }
            if (this.Status == Enums.ObjectStatusType.AlterRebuildDependenciesStatus)
            {
                GenerateDependencis();
                listDiff.AddRange(ToSQLDropDependencis());
                listDiff.AddRange(columns.ToSQLDiff());
                listDiff.AddRange(ToSQLCreateDependencis());
                listDiff.AddRange(constraints.ToSQLDiff());
                listDiff.AddRange(indexes.ToSQLDiff());
                listDiff.AddRange(options.ToSQLDiff());
                listDiff.AddRange(triggers.ToSQLDiff());
            }
            if (this.Status == Enums.ObjectStatusType.AlterStatus)
            {
                listDiff.AddRange(columns.ToSQLDiff());
                listDiff.AddRange(constraints.ToSQLDiff());
                listDiff.AddRange(indexes.ToSQLDiff());
                listDiff.AddRange(options.ToSQLDiff());
                listDiff.AddRange(triggers.ToSQLDiff());
            }
            if (this.Status == Enums.ObjectStatusType.AlterRebuildStatus)
            {
                GenerateDependencis();
                listDiff.AddRange(ToSQLRebuild());
                listDiff.AddRange(columns.ToSQLDiff());
                listDiff.AddRange(constraints.ToSQLDiff());
                listDiff.AddRange(indexes.ToSQLDiff());
                listDiff.AddRange(options.ToSQLDiff());
                //Como recrea la tabla, solo pone los nuevos triggers, por eso va ToSQL y no ToSQLDiff
                listDiff.Add(triggers.ToSQL(), dependenciesCount, Enums.ScripActionType.AddTrigger); 
            }
            return listDiff;
        }

        private string ToSQLTableRebuild()
        {
            string sql = "";
            string tempTable = "Temp" + Name;
            string listColumns = "";
            string listValues = "";
            Boolean IsIdentityNew = false;
            try
            {
                foreach (Column column in this.Columns)
                {
                    if ((column.Status != Enums.ObjectStatusType.DropStatus) && !((column.Status == Enums.ObjectStatusType.CreateStatus) && (column.Nullable == true)))
                    {
                        if ((!column.IsComputed) && (!column.Type.ToLower().Equals("timestamp")))
                        {
                            /*Si la nueva columna a agregar es XML, no se inserta ese campo y debe ir a la coleccion de Warnings*/
                            /*Si la nueva columna a agregar es Identity, tampoco se debe insertar explicitamente*/
                            if (!((column.Status == Enums.ObjectStatusType.CreateStatus) && ((column.Type.ToLower().Equals("xml") || (column.IsIdentity)))))
                            {
                                listColumns += "[" + column.Name + "],";
                                if (column.HasToForceValue)
                                {
                                    if (column.HasState(Enums.ObjectStatusType.UpdateStatus))
                                        listValues += "ISNULL([" + column.Name + "]," + column.DefaultForceValue + "),";
                                    else
                                        listValues += column.DefaultForceValue + ",";
                                }
                                else
                                    listValues += "[" + column.Name + "],";
                            }
                            else
                            {
                                if (column.IsIdentity) IsIdentityNew = true;
                            }
                        }
                    }
                }
                if (!String.IsNullOrEmpty(listColumns))
                {
                    listColumns = listColumns.Substring(0, listColumns.Length - 1);
                    listValues = listValues.Substring(0, listValues.Length - 1);
                    sql += ToSQLTemp(tempTable) + "\r\n";
                    if ((HasIdentityColumn) && (!IsIdentityNew))
                        sql += "SET IDENTITY_INSERT [" + Owner + "].[" + tempTable + "] ON\r\n";
                    sql += "INSERT INTO [" + Owner + "].[" + tempTable + "] (" + listColumns + ")" + " SELECT " + listValues + " FROM " + this.FullName + "\r\n";
                    if ((HasIdentityColumn) && (!IsIdentityNew))
                        sql += "SET IDENTITY_INSERT [" + Owner + "].[" + tempTable + "] OFF\r\nGO\r\n\r\n";
                    sql += "DROP TABLE " + this.FullName + "\r\nGO\r\n";
                    sql += "EXEC sp_rename N'[" + Owner + "].[" + tempTable + "]',N'" + this.Name + "', 'OBJECT'\r\nGO\r\n\r\n";
                    sql += OriginalTable.Options.ToSQL();
                }
                else
                    sql = "";
                return sql;
            }
            catch (Exception ex)
            {
                return "";
            }
        }

        private SQLScriptList ToSQLRebuild()
        {
            SQLScriptList listDiff = new SQLScriptList();            
            listDiff.AddRange(ToSQLDropDependencis());
            listDiff.Add(ToSQLTableRebuild(), dependenciesCount, Enums.ScripActionType.RebuildTable);
            listDiff.AddRange(ToSQLCreateDependencis());
            return listDiff;
        }

        private string ToSQLTemp(String TableName)
        {
            string sql = "";            
            sql += "CREATE TABLE [" + Owner + "].[" + TableName + "]\r\n(\r\n";
            
            this.Columns.Sort();
            
            for (int index = 0; index < this.Columns.Count; index++)
            {
                if (this.Columns[index].Status != Enums.ObjectStatusType.DropStatus)
                {
                    sql += "\t" + this.Columns[index].ToSQL(true);
                    if (index != this.Columns.Count - 1)
                        sql += ",";
                    sql += "\r\n";
                }
            }
            sql += ")";
            if (!String.IsNullOrEmpty(this.FileGroup)) sql += " ON [" + this.FileGroup + "]";
            if (!String.IsNullOrEmpty(FileGroupText))
            {
                if (this.HasBlobColumn)
                    sql += " TEXTIMAGE_ON [" + FileGroupText + "]";
            }
            sql += "\r\n";
            sql += "GO\r\n";
            return sql;
        }        

        private void GenerateDependencis()
        {
            List<ISchemaBase> myDependencis = null;
            /*Si el estado es AlterRebuildDependeciesStatus, busca las dependencias solamente en las columnas que fueron modificadas*/
            if (this.Status == Enums.ObjectStatusType.AlterRebuildDependenciesStatus)
            {
                myDependencis = new List<ISchemaBase>();
                for (int ic = 0; ic < this.Columns.Count; ic++)
                {
                    if ((this.Columns[ic].Status == Enums.ObjectStatusType.AlterRebuildDependenciesStatus) || (this.Columns[ic].Status == Enums.ObjectStatusType.AlterStatus))
                        myDependencis.AddRange(((Database)Parent).Dependencies.Find(this.Id, 0, this.Columns[ic].DataUserTypeId));
                }
                /*Si no encuentra ninguna, toma todas las de la tabla*/
                if (myDependencis.Count == 0)
                    myDependencis.AddRange(((Database)Parent).Dependencies.Find(this.Id));
            }
            else
                myDependencis = ((Database)Parent).Dependencies.Find(this.Id);

            dependencis = new List<ISchemaBase>();
            for (int j = 0; j < myDependencis.Count; j++)
            {
                ISchemaBase item = null;
                if (myDependencis[j].ObjectType == Enums.ObjectType.Index)
                    item = indexes[myDependencis[j].FullName];
                if (myDependencis[j].ObjectType == Enums.ObjectType.Constraint)
                    item = ((Database)Parent).Tables[myDependencis[j].Parent.FullName].Constraints[myDependencis[j].FullName];
                if (myDependencis[j].ObjectType == Enums.ObjectType.Default)
                    item = columns[myDependencis[j].FullName].Constraints[0];
                if (myDependencis[j].ObjectType == Enums.ObjectType.View)
                    item = ((Database)Parent).Views[myDependencis[j].FullName];
                if (myDependencis[j].ObjectType == Enums.ObjectType.Function)
                    item = ((Database)Parent).Functions[myDependencis[j].FullName];
                if (item != null)
                    dependencis.Add(item);
            }
        }

        /// <summary>
        /// Genera una lista de FK que deben ser eliminadas previamente a la eliminacion de la tablas.
        /// Esto pasa porque para poder eliminar una tabla, hay que eliminar antes todas las constraints asociadas.
        /// </summary>
        private SQLScriptList ToSQLDropFKBelow()
        {
            SQLScriptList listDiff = new SQLScriptList();
            constraints.ForEach (constraint => 
            {
                if ((constraint.Type == Constraint.ConstraintType.ForeignKey) && (((Table)constraint.Parent).DependenciesCount <= this.DependenciesCount))
                {
                    /*Si la FK pertenece a la misma tabla, no se debe explicitar el DROP CONSTRAINT antes de hacer el DROP TABLE*/
                    if (constraint.Parent.Id != constraint.RelationalTableId)
                    {
                        listDiff.Add(constraint.Drop());
                    }
                }
            });
            return listDiff;
        }

        /// <summary>
        /// Genera una lista de script de DROPS de todas los constraints dependientes de la tabla.
        /// Se usa cuando se quiere reconstruir una tabla y todos sus objectos dependientes.
        /// </summary>
        private SQLScriptList ToSQLDropDependencis()
        {
            SQLScriptList listDiff = new SQLScriptList();           
            //Se buscan todas las table constraints.
            for (int index = 0; index < dependencis.Count; index++)
            {
                if (dependencis[index].Status == Enums.ObjectStatusType.OriginalStatus)
                {
                    listDiff.Add(dependencis[index].Drop());
                }
            }
            //Se buscan todas las columns constraints.
            columns.ForEach(column => 
            {
                if (column.Constraints.Count > 0)
                {
                    if ((column.Constraints[0].Status == Enums.ObjectStatusType.OriginalStatus) && (column.Status != Enums.ObjectStatusType.CreateStatus))
                        listDiff.Add(column.Constraints[0].Drop());
                }
            });
            return listDiff;
        }

        private SQLScriptList ToSQLCreateDependencis()
        {
            SQLScriptList listDiff = new SQLScriptList();
            //Las constraints de deben recorrer en el orden inverso.
            for (int index = dependencis.Count - 1; index >= 0; index--)
            {
                if ((dependencis[index].Status == Enums.ObjectStatusType.OriginalStatus) && (dependencis[index].Parent.Status != Enums.ObjectStatusType.DropStatus))
                {
                    listDiff.Add(dependencis[index].Create());
                }
            }
            //Se buscan todas las columns constraints.
            for (int index = columns.Count - 1; index >= 0; index--)
            {
                if (columns[index].Constraints.Count > 0)
                {
                    if (columns[index].Constraints[0].CanCreate)
                        listDiff.Add(columns[index].Constraints[0].Create());
                }
            }
            return listDiff;
        }

        /// <summary>
        /// Indica la cantidad de Constraints dependientes de otra tabla (FK) que tiene
        /// la tabla.
        /// </summary>
        public override int DependenciesCount
        {
            get 
            {
                if (dependenciesCount == -1)
                    dependenciesCount = ((Database)Parent).Dependencies.DependenciesCount(this.Id, Enums.ObjectType.Constraint);
                return dependenciesCount; 
            }
            /*set 
            { 
                dependenciesCount = value; 
            }*/
        }

        /// <summary>
        /// Compara en primer orden por la operacion 
        /// (Primero van los Drops, luego los Create y finalesmente los Alter).
        /// Si la operacion es la misma, ordena por cantidad de tablas dependientes.
        /// </summary>
        public int CompareTo(Table other)
        {
            if (other == null) throw new ArgumentNullException("other");
            if (this.Status == other.Status)
                return this.DependenciesCount.CompareTo(other.DependenciesCount);
            else
                return other.Status.CompareTo(this.Status);
        }

        /// <summary>
        /// Compara dos tablas y devuelve true si son iguales, caso contrario, devuelve false.
        /// </summary>
        public static Boolean CompareFileGroup(Table origen, Table destino)
        {
            if (destino == null) throw new ArgumentNullException("destino");
            if (origen == null) throw new ArgumentNullException("origen");
            if ((!String.IsNullOrEmpty(destino.FileGroup) && (!String.IsNullOrEmpty(origen.FileGroup))))
                if (!destino.FileGroup.Equals(origen.FileGroup))
                    return false;
            return true;
        }

        /// <summary>
        /// Compara dos tablas y devuelve true si son iguales, caso contrario, devuelve false.
        /// </summary>
        public static Boolean CompareFileGroupText(Table origen, Table destino)
        {
            if (destino == null) throw new ArgumentNullException("destino");
            if (origen == null) throw new ArgumentNullException("origen");
            if ((!String.IsNullOrEmpty(destino.FileGroupText) && (!String.IsNullOrEmpty(origen.FileGroupText))))
                if (!destino.FileGroupText.Equals(origen.FileGroupText))
                    return false;
            return true;
        }
    }
}
