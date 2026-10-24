using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Range les tuiles en cellules, une tuile agrandie en occupant plusieurs.
    ///
    /// ## Le défaut qu'il corrige
    ///
    /// Un <c>WrapPanel</c> ne sait faire que des rangées, et la hauteur d'une
    /// rangée vaut celle de son plus haut élément. Une tuile triple y imposait
    /// sa hauteur à toute la rangée : mesuré, 42 % de cette rangée perdus,
    /// l'équivalent de onze tuiles normales de surface.
    ///
    /// Ici une tuile réclame le nombre entier de cellules qu'il lui faut, et
    /// les tuiles normales comblent ce qui reste. Deux rangées de normales se
    /// rangent à côté d'une double, trois à côté d'une triple.
    ///
    /// ## Pourquoi la tuile ne remplit pas tout à fait son bloc
    ///
    /// Depuis le lot 6b, l'agrandissement ne porte que sur les courbes : le
    /// bandeau de nom et les bordures gardent leur taille. La hauteur d'une
    /// tuile vaut donc <c>56n + 33</c> et non <c>89n</c> — 145 px pour une
    /// double, là où deux cellules en font 178. Elle réclame bien deux
    /// cellules, qu'elle ne remplit qu'aux quatre cinquièmes.
    ///
    /// C'est assumé : la bande de 33 px laissée sous une tuile agrandie est
    /// sans commune mesure avec la rangée entière que coûtait le WrapPanel. La
    /// faire disparaître demanderait d'étirer les courbes d'un facteur non
    /// entier — 2,59 au lieu de 2 — et ramènerait les demi-pixels sur des
    /// barres de cinq pixels de large, que les multiples entiers avaient
    /// justement écartés.
    ///
    /// ## Le coût
    ///
    /// Le placement s'exécute à chaque passe de mise en page, et il s'en
    /// produit une par relevé — l'écriture d'une hauteur de barre marque toute
    /// la chaîne d'ancêtres à remesurer, WPF ne court-circuitant pas même
    /// lorsque la taille ne peut pas changer. C'est donc le coût par relevé qui
    /// compte, et il est mesuré de part et d'autre dans la description de la PR.
    /// </summary>
    public class TilePanel : Panel
    {
        /// <summary>
        /// Dimensions de la cellule, déduites de la plus petite tuile visible.
        ///
        /// Déduites et non constantes : le panneau n'a pas à connaître la
        /// géométrie de la tuile, qui a déjà changé deux fois. La plus petite
        /// est la tuile à l'échelle normale, qui est de loin la plus fréquente.
        /// </summary>
        private double _cellWidth = 1d;
        private double _cellHeight = 1d;

        /// <summary>
        /// Placement retenu par la mesure, réutilisé tel quel par l'arrangement :
        /// recalculer le pavage une seconde fois donnerait le même résultat pour
        /// le même prix.
        /// </summary>
        private readonly List<Placement> _placements = new List<Placement>();

        private struct Placement
        {
            public UIElement Element;
            public int Column;
            public int Row;
        }

        /// <summary>Nombre de colonnes du dernier pavage. Pour les vérifications.</summary>
        public int Columns { get; private set; }

        /// <summary>Nombre de rangées occupées. Pour les vérifications.</summary>
        public int Rows { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            this._placements.Clear();

            double cellWidth = double.MaxValue;
            double cellHeight = double.MaxValue;

            Size unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);

            foreach (UIElement child in this.InternalChildren)
            {
                child.Measure(unbounded);

                // Une tuile filtrée est repliée : elle ne prend pas de cellule,
                // et surtout elle ne doit pas rapetisser la cellule à zéro.
                if (child.Visibility != Visibility.Visible)
                {
                    continue;
                }

                Size desired = child.DesiredSize;

                if (desired.Width > 0d && desired.Width < cellWidth)
                {
                    cellWidth = desired.Width;
                }

                if (desired.Height > 0d && desired.Height < cellHeight)
                {
                    cellHeight = desired.Height;
                }
            }

            if (cellWidth == double.MaxValue || cellHeight == double.MaxValue)
            {
                this.Columns = 0;
                this.Rows = 0;

                return new Size(0d, 0d);
            }

            this._cellWidth = cellWidth;
            this._cellHeight = cellHeight;

            double usableWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0d
                ? cellWidth
                : availableSize.Width;

            int columns = (int)(usableWidth / cellWidth);

            if (columns < 1)
            {
                columns = 1;
            }

            this.Columns = columns;

            Occupancy occupancy = new Occupancy(columns);

            foreach (UIElement child in this.InternalChildren)
            {
                if (child.Visibility != Visibility.Visible)
                {
                    continue;
                }

                Size desired = child.DesiredSize;

                int width = CellsFor(desired.Width, cellWidth);
                int height = CellsFor(desired.Height, cellHeight);

                if (width > columns)
                {
                    // Une tuile plus large que le panneau prend toute la
                    // largeur plutôt que de ne jamais trouver de place.
                    width = columns;
                }

                int column;
                int row;

                occupancy.Place(width, height, out column, out row);

                this._placements.Add(new Placement { Element = child, Column = column, Row = row });
            }

            this.Rows = occupancy.UsedRows;

            return new Size(columns * cellWidth, occupancy.UsedRows * cellHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (Placement placement in this._placements)
            {
                Size desired = placement.Element.DesiredSize;

                // La tuile est posée à sa taille, non étirée à celle de son
                // bloc : c'est ce qui laisse la bande sous une tuile agrandie,
                // et c'est aussi ce qui empêche une tuile normale d'être
                // déformée par une voisine plus haute.
                placement.Element.Arrange(new Rect(
                    placement.Column * this._cellWidth,
                    placement.Row * this._cellHeight,
                    desired.Width,
                    desired.Height));
            }

            return finalSize;
        }

        private static int CellsFor(double size, double cell)
        {
            if (size <= 0d || cell <= 0d)
            {
                return 0;
            }

            // Une marge d'un demi-pixel absorbe les arrondis de mise en page :
            // sans elle, une tuile de 108,0000001 px réclamerait deux cellules.
            int cells = (int)Math.Ceiling((size - 0.5d) / cell);

            return cells < 1 ? 1 : cells;
        }

        /// <summary>
        /// Carte d'occupation, une ligne de bits par rangée.
        ///
        /// Le premier-ajusté repart du premier rang encore libre et non du
        /// début : une rangée entièrement occupée ne peut plus se libérer,
        /// puisqu'on ne fait qu'ajouter. Sans ce curseur, le placement serait
        /// quadratique sur quatre cents tuiles.
        /// </summary>
        private sealed class Occupancy
        {
            private readonly int _columns;
            private readonly List<bool[]> _rows = new List<bool[]>();

            private int _firstFreeRow;

            public Occupancy(int columns)
            {
                this._columns = columns;
            }

            public int UsedRows
            {
                get { return this._rows.Count; }
            }

            public void Place(int width, int height, out int column, out int row)
            {
                for (int candidateRow = this._firstFreeRow; ; candidateRow++)
                {
                    EnsureRow(candidateRow + height - 1);

                    for (int candidateColumn = 0; candidateColumn + width <= this._columns; candidateColumn++)
                    {
                        if (!IsFree(candidateColumn, candidateRow, width, height))
                        {
                            continue;
                        }

                        Fill(candidateColumn, candidateRow, width, height);

                        column = candidateColumn;
                        row = candidateRow;

                        AdvanceFirstFreeRow();

                        return;
                    }
                }
            }

            private void AdvanceFirstFreeRow()
            {
                while (this._firstFreeRow < this._rows.Count && IsRowFull(this._firstFreeRow))
                {
                    this._firstFreeRow++;
                }
            }

            private bool IsRowFull(int row)
            {
                bool[] cells = this._rows[row];

                for (int i = 0; i < this._columns; i++)
                {
                    if (!cells[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            private void EnsureRow(int row)
            {
                while (this._rows.Count <= row)
                {
                    this._rows.Add(new bool[this._columns]);
                }
            }

            private bool IsFree(int column, int row, int width, int height)
            {
                for (int y = row; y < row + height; y++)
                {
                    bool[] cells = this._rows[y];

                    for (int x = column; x < column + width; x++)
                    {
                        if (cells[x])
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            private void Fill(int column, int row, int width, int height)
            {
                for (int y = row; y < row + height; y++)
                {
                    bool[] cells = this._rows[y];

                    for (int x = column; x < column + width; x++)
                    {
                        cells[x] = true;
                    }
                }
            }
        }
    }
}
