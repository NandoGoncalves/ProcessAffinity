using System;
using System.Windows.Input;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Curseur d'attente le temps d'une opération qui bloque le thread de
    /// l'affichage.
    ///
    /// Le rétablissement passe par <c>Dispose</c>, donc par le <c>finally</c> que
    /// le compilateur pose autour d'un <c>using</c> : une exception ne peut pas
    /// laisser le sablier en place. Le bouton « Load » posait son curseur à la
    /// main et le rendait après le <c>catch</c> ; il suffisait d'un échec dans le
    /// traitement de l'erreur pour que l'application reste en attente perpétuelle.
    ///
    /// Le rétablissement remet <c>null</c>, et non <c>Cursors.Arrow</c> : forcer
    /// la flèche écrase les curseurs propres à chaque contrôle, et le champ de
    /// recherche perdait ainsi son pointeur de saisie jusqu'au redémarrage.
    /// </summary>
    internal sealed class WaitCursor : IDisposable
    {
        private readonly Cursor _previous;
        private bool _restored;

        private WaitCursor()
        {
            this._previous = Mouse.OverrideCursor;

            Mouse.OverrideCursor = Cursors.Wait;
        }

        /// <summary>
        /// Pose le curseur d'attente. À utiliser dans un <c>using</c>.
        /// </summary>
        public static WaitCursor Show()
        {
            return new WaitCursor();
        }

        public void Dispose()
        {
            if (this._restored)
            {
                return;
            }

            this._restored = true;

            // Ce qui était en place avant, le plus souvent null : imbriquer deux
            // opérations ne doit pas rendre la main trop tôt.
            Mouse.OverrideCursor = this._previous;
        }
    }
}
